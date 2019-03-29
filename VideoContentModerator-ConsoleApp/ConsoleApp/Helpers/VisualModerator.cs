/*
 * Copyright (c) 2019
 * Released under the MIT license
 * http://opensource.org/licenses/mit-license.php
 */

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.Management.Media;
using Microsoft.Azure.Management.Media.Models;
using Microsoft.Rest.Azure.Authentication;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.IdentityModel.Clients.ActiveDirectory;


namespace Microsoft.ContentModerator.VideoContentModerator
{
    public class VisualModerator
    {
        const String VisualModerationTransformName = "VisualModerationTransform";
        private VisualModeratorConfig visualModeratorConfig;

        public VisualModerator(ConfigWrapper config)
        {
            this.visualModeratorConfig = config.visualModerator;
        }

        public async Task<VisualModerationAsset> ModerateVideo(string videoPath)
        {
            string resourceGroup = this.visualModeratorConfig.ResourceGroup;
            string accountName = this.visualModeratorConfig.AccountName;

            IAzureMediaServicesClient client = await CreateMediaServicesClientAsync();
            VisualModerationAsset result = null;

            try
            {
                string uniqueness = Guid.NewGuid().ToString();
                string jobName = "job-" + uniqueness;
                string inputAssetName = "asset-input-" + uniqueness;
                string outputAssetName1 = "asset-output-video-" + uniqueness;
                string outputAssetName2 = "asset-output-analysis-" + uniqueness;
                string streamingLocatorName1 = "streaminglocator-video-" + uniqueness;
                string streamingLocatorName2 = "streaminglocator-analysis-" + uniqueness;

                Transform videoAnalyzerTransform = EnsureTransformExists(client, resourceGroup, accountName, VisualModerationTransformName);

                CreateInputAsset(client, resourceGroup, accountName, inputAssetName, videoPath).Wait();
                Asset outputAsset1 = CreateOutputAsset(client, resourceGroup, accountName, outputAssetName1);
                Asset outputAsset2 = CreateOutputAsset(client, resourceGroup, accountName, outputAssetName2);
                JobInput jobInput = new JobInputAsset(assetName: inputAssetName);
                JobOutput[] jobOutputs = {
                    new JobOutputAsset(outputAsset1.Name),
                    new JobOutputAsset(outputAsset2.Name),
                };

                Job job = SubmitJob(client, resourceGroup, accountName, VisualModerationTransformName, jobName, jobInput, jobOutputs);
                job = WaitForJobToFinish(client, resourceGroup, accountName, VisualModerationTransformName, jobName);

                if (job.State == JobState.Finished)
                {
                    Console.WriteLine("\nAMSv3 Job finished.");
                    PublishStreamingAsset(client, resourceGroup, accountName, outputAsset1.Name, streamingLocatorName1);
                    PublishDownloadAsset(client, resourceGroup, accountName, outputAsset2.Name, streamingLocatorName2);
                    result = CreateVisualModeratorResult(client, resourceGroup, accountName, streamingLocatorName1, streamingLocatorName2);
                    result.VideoName = outputAssetName1;
                    result.VideoFilePath = videoPath;
                    result.AccessToken = null;
                    result.VisualModeratedJson = await GetVisualModerationJsonFile(client, resourceGroup, accountName, outputAsset2.Name);
                }
                else if (job.State == JobState.Error)
                {
                    Console.WriteLine($"ERROR: Job finished with error message: {job.Outputs[0].Error.Message}");
                    Console.WriteLine($"ERROR:                   error details: {job.Outputs[0].Error.Details[0].Message}");
                }

            }
            catch (ApiErrorException ex)
            {
                string code = ex.Body.Error.Code;
                string message = ex.Body.Error.Message;

                Console.WriteLine("ERROR:API call failed with error code: {0} and message: {1}", code, message);
            }

            return result;
        }

        private async Task<IAzureMediaServicesClient> CreateMediaServicesClientAsync()
        {
            ClientCredential clientCredential = new ClientCredential(this.visualModeratorConfig.AadClientId, this.visualModeratorConfig.AadClientSecret);
            var credentials = await ApplicationTokenProvider.LoginSilentAsync(this.visualModeratorConfig.AadTenantId, clientCredential, ActiveDirectoryServiceSettings.Azure);

            return new AzureMediaServicesClient(this.visualModeratorConfig.ArmEndpoint, credentials)
            {
                SubscriptionId = this.visualModeratorConfig.SubscriptionId
            };
        }

        private Transform EnsureTransformExists(IAzureMediaServicesClient client, string resourceGroupName, string accountName, string transformName)
        {
            Transform transform = client.Transforms.Get(resourceGroupName, accountName, transformName);

            if (transform == null)
            {
                TransformOutput[] outputs = new TransformOutput[]
                {
                    new TransformOutput(new BuiltInStandardEncoderPreset(EncoderNamedPreset.AdaptiveStreaming)),
                    new TransformOutput(new VideoAnalyzerPreset()),
                };

                transform = client.Transforms.CreateOrUpdate(resourceGroupName, accountName, transformName, outputs);
                Console.WriteLine("AMSv3 Transform has been created: ", transformName);
            }

            return transform;
        }

        private async Task<Asset> CreateInputAsset(IAzureMediaServicesClient client, string resourceGroupName, string accountName, string assetName, string fileToUpload)
        {
            Asset asset = client.Assets.CreateOrUpdate(resourceGroupName, accountName, assetName, new Asset());

            ListContainerSasInput input = new ListContainerSasInput()
            {
                Permissions = AssetContainerPermission.ReadWrite,
                ExpiryTime = DateTime.Now.AddHours(2).ToUniversalTime()
            };

            var response = client.Assets.ListContainerSasAsync(resourceGroupName, accountName, assetName, input.Permissions, input.ExpiryTime).Result;

            string uploadSasUrl = response.AssetContainerSasUrls.First();

            string filename = Path.GetFileName(fileToUpload);
            Console.WriteLine("Uploading file: {0}", filename);

            var sasUri = new Uri(uploadSasUrl);
            CloudBlobContainer container = new CloudBlobContainer(sasUri);
            var blob = container.GetBlockBlobReference(filename);
            blob.Properties.ContentType = "video/mp4";
            Console.WriteLine("Uploading File to AMSv3 asset container: {0}", sasUri);
            await blob.UploadFromFileAsync(fileToUpload);

            return asset;
        }

        private Asset CreateOutputAsset(IAzureMediaServicesClient client, string resourceGroupName, string accountName, string assetName)
        {
            return client.Assets.CreateOrUpdate(resourceGroupName, accountName, assetName, new Asset());
        }

        private Job SubmitJob(IAzureMediaServicesClient client, string resourceGroupName, string accountName, string transformName, string jobName, JobInput jobInput, JobOutput[] jobOutputs)
        {
            Job job = client.Jobs.Create(
                resourceGroupName,
                accountName,
                transformName,
                jobName,
                new Job
                {
                    Input = jobInput,
                    Outputs = jobOutputs
                });

            Console.WriteLine("AMSv3 Job has been submitted.");
            return job;
        }

        private Job WaitForJobToFinish(IAzureMediaServicesClient client, string resourceGroupName, string accountName, string transformName, string jobName)
        {
            const int SleepInterval = 10 * 1000;

            Job job = null;
            bool exit = false;

            Console.Write("AMSv3 Job is running");
            do
            {
                job = client.Jobs.Get(resourceGroupName, accountName, transformName, jobName);

                if (job.State == JobState.Finished || job.State == JobState.Error || job.State == JobState.Canceled)
                {
                    exit = true;
                }
                else
                {
                    for (int i = 0; i < job.Outputs.Count; i++)
                    {
                        JobOutput output = job.Outputs[i];
                        if (output.State == JobState.Processing)
                        {
                            Console.Write(".");
                        }
                    }
                    System.Threading.Thread.Sleep(SleepInterval);
                }
            }
            while (!exit);

            return job;
        }

        private void PublishStreamingAsset(IAzureMediaServicesClient client, string resourceGroup, string accountName, string assetName, string streamingLocatorName)
        {
            StreamingLocator locator = new StreamingLocator(
                assetName: assetName,
                streamingPolicyName: PredefinedStreamingPolicy.ClearStreamingOnly);

            client.StreamingLocators.Create(resourceGroup, accountName, streamingLocatorName, locator);
        }

        private void PublishDownloadAsset(IAzureMediaServicesClient client, string resourceGroup, string accountName, string assetName, string streamingLocatorName)
        {
            StreamingLocator locator = new StreamingLocator(
                assetName: assetName,
                streamingPolicyName: PredefinedStreamingPolicy.DownloadOnly);

            client.StreamingLocators.Create(resourceGroup, accountName, streamingLocatorName, locator);
        }

        private VisualModerationAsset CreateVisualModeratorResult(IAzureMediaServicesClient client, string resourceGroup, string accountName, string videoStreamingLocatorName, string analysisStreamingLocatorName)
        {
            VisualModerationAsset result = new VisualModerationAsset();
            result.StreamingUrlDetails = new PublishedUrlDetails();

            var streamingEndpoint = client.StreamingEndpoints.Get(resourceGroup, accountName, "default");
            UriBuilder uriBuilder = new UriBuilder();
            uriBuilder.Scheme = "https";
            uriBuilder.Host = streamingEndpoint.HostName;

            var paths = client.StreamingLocators.ListPaths(resourceGroup, accountName, videoStreamingLocatorName);
            for (int i = 0; i < paths.StreamingPaths.Count; i++)
            {
                if (paths.StreamingPaths[i].Paths.Count > 0)
                {
                    if (paths.StreamingPaths[i].StreamingProtocol == StreamingPolicyStreamingProtocol.SmoothStreaming)
                    {
                        uriBuilder.Path = paths.StreamingPaths[i].Paths[0];
                        result.StreamingUrlDetails.SmoothUri = uriBuilder.ToString();
                    }
                    else if (paths.StreamingPaths[i].StreamingProtocol == StreamingPolicyStreamingProtocol.Dash)
                    {
                        uriBuilder.Path = paths.StreamingPaths[i].Paths[0];
                        result.StreamingUrlDetails.MpegDashUri = uriBuilder.ToString();
                    }
                    else if (paths.StreamingPaths[i].StreamingProtocol == StreamingPolicyStreamingProtocol.Hls)
                    {
                        uriBuilder.Path = paths.StreamingPaths[i].Paths[0];
                        result.StreamingUrlDetails.HlsUri = uriBuilder.ToString();
                    }
                }
            }
            var dlpaths = client.StreamingLocators.ListPaths(resourceGroup, accountName, analysisStreamingLocatorName);
            for (int i = 0; i < dlpaths.DownloadPaths.Count; i++)
            {
                if (dlpaths.DownloadPaths[i].EndsWith("transcript.vtt"))
                {
                    uriBuilder.Path = dlpaths.DownloadPaths[i];
                    result.StreamingUrlDetails.VttUrl = uriBuilder.ToString();
                }
            }
            return result;
        }

        private async Task<string> GetVisualModerationJsonFile(IAzureMediaServicesClient client, string resourceGroupName, string accountName, string assetName)
        {
            ListContainerSasInput parameters = new ListContainerSasInput();
            AssetContainerSas assetContainerSas = client.Assets.ListContainerSas(resourceGroupName, accountName, assetName, permissions: AssetContainerPermission.Read, expiryTime: DateTime.UtcNow.AddHours(1).ToUniversalTime());

            Uri containerSasUrl = new Uri(assetContainerSas.AssetContainerSasUrls.FirstOrDefault());
            CloudBlobContainer container = new CloudBlobContainer(containerSasUrl);

            var blobs = container.ListBlobsSegmentedAsync(null, true, BlobListingDetails.None, 200, null, null, null).Result;

            string jsonString = null;
            foreach (var blobItem in blobs.Results)
            {
                if (blobItem is CloudBlockBlob)
                {
                    CloudBlockBlob blob = blobItem as CloudBlockBlob;
                    if (blob.Name == "contentmoderation.json")
                    {
                        jsonString = await blob.DownloadTextAsync();
                    }
                }
            }
            return jsonString;
        }

    }
}
