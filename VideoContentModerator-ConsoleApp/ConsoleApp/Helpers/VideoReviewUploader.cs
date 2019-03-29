/*
 * Copyright (c) 2019
 * Released under the MIT license
 * http://opensource.org/licenses/mit-license.php
 */

using Microsoft.Azure.CognitiveServices.ContentModerator;
using Microsoft.Azure.CognitiveServices.ContentModerator.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Mime;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace Microsoft.ContentModerator.VideoContentModerator
{
    public class VideoReviewUploader
    {
        private static readonly string UnpublishedStatus = "UnPublished";
        private static readonly string VideoEntityType = "Video";
        private static readonly int SegmentSizeOfFrameEvents = 500;

        private ReviewToolConfig reviewToolConfig;
        private ContentModeratorClient client;
        private List<FrameEvent> frameEventList;

        public VideoReviewUploader(ConfigWrapper config)
        {
            this.reviewToolConfig = config.reviewTool;
            this.client = new ContentModeratorClient(new ApiKeyServiceClientCredentials(reviewToolConfig.ApiSubscriptionKey))
            {
                Endpoint = reviewToolConfig.ApiEndpoint
            };
        }

        public async Task<string> CreateVideoReview(VisualModerationAsset result)
        {
            // Step 1 - Create Video Review

            string reviewId = string.Empty;
            List<FrameEvent> frameEventList = ExtractFrameEventListFromJson(result.VisualModeratedJson);

            reviewId = await CreateVideoReviewWithContentModeratorReviewAPI(result, frameEventList);
            this.frameEventList = FixFrameNameInFrameEventList(frameEventList, reviewId);

            return reviewId;
        }

        public async Task<string> AddVideoFramesToVideoReview(VisualModerationAsset result, string reviewId)
        {
            // Step 2 - Add Video Frames to Video Review

            bool isSuccess = true;

            List<List<FrameEvent>> segmentedFrameEventList = GenerateBatchSegmentsOfFrameEventList(this.frameEventList, SegmentSizeOfFrameEvents);
            string frameZipFilesPath = GenerateFrameImages(segmentedFrameEventList, reviewId, result.VideoFilePath);
            isSuccess = await AddVideoFramesToReviewWithContentModeratorReviewAPI(reviewId, segmentedFrameEventList, frameZipFilesPath);
            if (!isSuccess)
            {
                throw new Exception("AddVideoFramesToReviewWithContentModeratorReviewAPI call failed.");
            }

            return reviewId;
        }

        public async Task<string> PublishVideoReview(string reviewId)
        {
            // Step 3 - Publish Video Review
            bool isSuccess = true;

            isSuccess = await PublishVideoReviewWithContentModeratorReviewAPI(reviewId);
            if (!isSuccess)
            {
                throw new Exception("PublishVideoReviewWithContentModeratorReviewAPI call failed.");
            }

            return reviewId;
        }

        #region ### Create Video Review operations

        public async Task<string> CreateVideoReviewWithContentModeratorReviewAPI(VisualModerationAsset result, List<FrameEvent> frameEventList)
        {
            string reviewId = string.Empty;
            try
            {
                string videoReviewsJson = CreateVideoReviewObject(result, frameEventList);
                if (string.IsNullOrWhiteSpace(videoReviewsJson))
                {
                    throw new Exception("VideoReview process failed in CreateVideoReview");
                }

                List<CreateVideoReviewsBodyItem> review = JsonConvert.DeserializeObject<List<CreateVideoReviewsBodyItem>>(videoReviewsJson);
                var res = await this.client.Reviews.CreateVideoReviewsWithHttpMessagesAsync("application/json", reviewToolConfig.TeamId, review);
                if (res.Response.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    throw new Exception("CreateVideoReviewWithContentModeratorReviewAPI has failed to get a Video Review. Code: " + res.Response.StatusCode);
                }
                List<string> reviewIds = res.Body.ToList();
                reviewId = reviewIds.FirstOrDefault();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                throw;
            }
            return reviewId;
        }

        private string CreateVideoReviewObject(VisualModerationAsset result, List<FrameEvent> frameEventList)
        {
            List<VideoReview> videoReviews = GenerateVideoReviewList(result, frameEventList);
            return JsonConvert.SerializeObject(videoReviews);
        }

        private List<VideoReview> GenerateVideoReviewList(VisualModerationAsset result, List<FrameEvent> frameEventList)
        {
            List<VideoReview> videoReviewList = new List<VideoReview>();
            VideoReview videoReviewObj = new VideoReview();

            videoReviewObj.Type = VideoEntityType;
            videoReviewObj.Content = result.StreamingUrlDetails.SmoothUri;
            videoReviewObj.ContentId = result.VideoName;
            videoReviewObj.CallbackEndpoint = null;
            videoReviewObj.Metadata = frameEventList.Count != 0 ? GenerateVideoReviewMetadata(frameEventList) : null; ;
            videoReviewObj.Status = UnpublishedStatus;
            videoReviewObj.VideoFrames = null;
            videoReviewList.Add(videoReviewObj);

            return videoReviewList;
        }

        private List<Metadata> GenerateVideoReviewMetadata(List<FrameEvent> frameEventList)
        {
            List<Metadata> metadata = new List<Metadata>();
            var adultScore = frameEventList.OrderByDescending(a => Double.Parse(a.AdultScore)).FirstOrDefault().AdultScore;
            var racyScore = frameEventList.OrderByDescending(a => Double.Parse(a.RacyScore)).FirstOrDefault().RacyScore;
            var isAdult = double.Parse(adultScore) > reviewToolConfig.AdultFrameThreshold ? true : false;
            var isRacy = double.Parse(racyScore) > reviewToolConfig.RacyFrameThreshold ? true : false;
            var reviewRecommended = frameEventList.Any(frame => frame.ReviewRecommended);
            metadata = new List<Metadata>()
            {
                new Metadata() {Key = "ReviewRecommended", Value = reviewRecommended.ToString()},
                new Metadata() {Key = "AdultScore", Value = adultScore},
                new Metadata() {Key = "a", Value = isAdult.ToString() },
                new Metadata() {Key = "RacyScore", Value = racyScore},
                new Metadata() {Key = "r", Value = isRacy.ToString() }
            };
            return metadata;
        }

        #endregion


        #region ### Add Video Frames operations

        public async Task<bool> AddVideoFramesToReviewWithContentModeratorReviewAPI(string reviewId, List<List<FrameEvent>> segmentedFrameEventList, string frameZipFilesPath)
        {
            bool isSuccess = false;
            try
            {
                List<string> addVideoFrameRequests = new List<string>();
                foreach (var frameEventListSegment in segmentedFrameEventList)
                {
                    addVideoFrameRequests.Add(CreateAddVideoFramesReviewObject(frameEventListSegment));
                }

                DirectoryInfo d = new DirectoryInfo(frameZipFilesPath);
                FileInfo[] frameImagesZipFiles = d.GetFiles();

                for (int i = 0; i < addVideoFrameRequests.Count; i++)
                {
                    Stream frameImagesZip = new FileStream(frameImagesZipFiles[i].FullName, FileMode.Open, FileAccess.Read);
                    var ContentType = new MediaTypeHeaderValue("application/x-zip-compressed");
                    var res = await this.client.Reviews.AddVideoFrameStreamWithHttpMessagesAsync(ContentType.ToString(), reviewToolConfig.TeamId, reviewId, frameImagesZip, addVideoFrameRequests[i]);
                    isSuccess = res.Response.IsSuccessStatusCode;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                throw;
            }
            return isSuccess;
        }

        private string CreateAddVideoFramesReviewObject(List<FrameEvent> frameEventList)
        {
            List<VideoFrame> videoFrames = GenerateVideoFrameList(frameEventList);
            return JsonConvert.SerializeObject(videoFrames);
        }

        private List<VideoFrame> GenerateVideoFrameList(List<FrameEvent> frameEventList)
        {
            List<VideoFrame> videoFrames = new List<VideoFrame>();
            foreach (FrameEvent frameEvent in frameEventList)
            {
                VideoFrame videoFrameObj = new VideoFrame()
                {
                    Timestamp = Convert.ToString(frameEvent.TimeStamp),
                    FrameImage = frameEvent.FrameName,
                    Metadata = new List<Metadata>()
                    {
                        new Metadata() {Key = "Review Recommended", Value = frameEvent.ReviewRecommended.ToString()},
                        new Metadata() {Key = "Adult Score", Value = frameEvent.AdultScore},
                        new Metadata() {Key = "a", Value = frameEvent.IsAdultContent.ToString()},
                        new Metadata() {Key = "Racy Score", Value = frameEvent.RacyScore},
                        new Metadata() {Key = "r", Value = frameEvent.IsRacyContent.ToString()},
                        new Metadata() {Key = "ExternalId", Value = frameEvent.FrameName},
                    },
                    ReviewerResultTags = new List<ReviewResultTag>(),
                };
                videoFrames.Add(videoFrameObj);
            }
            return videoFrames;
        }

        #endregion


        #region ### Publish Video Review operations

        public async Task<bool> PublishVideoReviewWithContentModeratorReviewAPI(string reviewId)
        {
            string resultJson = string.Empty;
            try
            {
                var res = await this.client.Reviews.PublishVideoReviewWithHttpMessagesAsync(reviewToolConfig.TeamId, reviewId);
                return res.Response.IsSuccessStatusCode;
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                throw;
            }
        }

        #endregion


        #region ### FrameEvent related operations

        private List<FrameEvent> ExtractFrameEventListFromJson(string contentModerationJsonString)
        {
            List<FrameEvent> outputFrameEventsList = new List<FrameEvent>();
            var contentModerationJsonObject = JsonConvert.DeserializeObject<VisualContentModerationJson>(contentModerationJsonString);

            if (contentModerationJsonObject != null)
            {
                var timeScale = Convert.ToInt32(contentModerationJsonObject.TimeScale);
                int frameCount = 0;
                foreach (var item in contentModerationJsonObject.Fragments)
                {
                    if (item.Events != null)
                    {
                        foreach (var frameEventList in item.Events)
                        {
                            foreach (FrameEvent frameEvent in frameEventList)
                            {
                                var eventObj = new FrameEvent
                                {
                                    ReviewRecommended = frameEvent.ReviewRecommended,
                                    TimeStamp = (frameEvent.TimeStamp * 1000 / timeScale),
                                    IsAdultContent = double.Parse(frameEvent.AdultScore) > reviewToolConfig.AdultFrameThreshold ? true : false,
                                    AdultScore = frameEvent.AdultScore,
                                    IsRacyContent = double.Parse(frameEvent.RacyScore) > reviewToolConfig.RacyFrameThreshold ? true : false,
                                    RacyScore = frameEvent.RacyScore,
                                    TimeScale = timeScale,
                                };
                                frameCount++;
                                eventObj.FrameName = "_" + frameCount + ".jpg";
                                outputFrameEventsList.Add(eventObj);
                            }
                        }
                    }
                }
            }
            return outputFrameEventsList;
        }

        private List<FrameEvent> FixFrameNameInFrameEventList(List<FrameEvent> frameEventList, string reviewId)
        {
            foreach (var frame in frameEventList)
            {
                frame.FrameName = reviewId + frame.FrameName;
            }
            return frameEventList;
        }

        private List<List<FrameEvent>> GenerateBatchSegmentsOfFrameEventList(List<FrameEvent> frameEventList, int batchSize)
        {
            List<List<FrameEvent>> batchFrames = new List<List<FrameEvent>>();
            while (frameEventList.Count > 0)
            {
                if (batchSize < frameEventList.Count)
                {
                    batchFrames.Add(frameEventList.Take(batchSize).ToList());
                    frameEventList.RemoveRange(0, batchSize);
                }
                else
                {
                    batchFrames.Add(frameEventList.Take(frameEventList.Count).ToList());
                    frameEventList.Clear();
                }
            }
            return batchFrames;
        }

        private string GenerateFrameImages(List<List<FrameEvent>> segmetedFrameEventList, string reviewId, string videoFilePath)
        {
            List<FFmpegVideoFrameImage> videoFrameImages = new List<FFmpegVideoFrameImage>();

            int frameEventsSegment = 0;
            foreach (var frameEventList in segmetedFrameEventList)
            {
                foreach (var frame in frameEventList)
                {
                    FFmpegVideoFrameImage frameImage = new FFmpegVideoFrameImage();
                    frameImage.VideoFilePath = videoFilePath;
                    frameImage.FrameImagesPath = "\\" + frameEventsSegment;
                    frameImage.FrameImageFilePath = frameImage.FrameImagesPath + "\\" + frame.FrameName;
                    frameImage.FramePosition = TimeSpan.FromMilliseconds(Convert.ToDouble(frame.TimeStamp));

                    videoFrameImages.Add(frameImage);
                }
                frameEventsSegment++;
            }

            return FFmpegComponent.GenerateFrameImages(videoFrameImages, reviewId, segmetedFrameEventList.Count);
        }

        #endregion

    }
}
