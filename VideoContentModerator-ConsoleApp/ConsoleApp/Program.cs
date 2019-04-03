/*
 * Copyright (c) 2019
 * Released under the MIT license
 * http://opensource.org/licenses/mit-license.php
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;


namespace Microsoft.ContentModerator.VideoContentModerator
{
    public class Program
    {
        static TextModerator textModerator;
        static VisualModerator visualModerator;
        static VideoReviewUploader videoReviewUploader;


        public static async Task Main(string[] args)
        {
            ConfigWrapper config = new ConfigWrapper(new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build());

            textModerator = new TextModerator(config);
            visualModerator = new VisualModerator(config);
            videoReviewUploader = new VideoReviewUploader(config);

            if (args.Length == 0)
            {
                string videoPath = string.Empty;
                GetUserInputs(out videoPath);
                try
                {
                    await ProcessVideo(videoPath);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }
            else
            {
                DirectoryInfo directoryInfo = new DirectoryInfo(args[0]);
                var files = directoryInfo.GetFiles("*.mp4", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    try
                    {
                        await ProcessVideo(file.FullName);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.Message);
                    }
                }
            }
        }

        private static async Task ProcessVideo(string videoPath)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();

            // Generate a proxy video file
            var compressedVideoPath = GenerateProxyVideo(videoPath);

            // Visual moderation with the proxy video file
            var vmResult = await ProcessVideoModeration(compressedVideoPath);

            // Text moderation with VTT file
            var tmResult = await ProcessTextModeration(vmResult.StreamingUrlDetails.VttUrl);

            // Create a Review Item & Upload moderation results
            await UploadVideoReview(vmResult, tmResult);

            watch.Stop();
            Console.WriteLine("Video review successfully completed...");
            Console.WriteLine("Total Elapsed Time: {0}", watch.Elapsed);
        }

        private static void GetUserInputs(out string videoPath)
        {
            Console.WriteLine("Enter the fully qualified local path for Uploading the video : \n ");
            videoPath = Console.ReadLine().Replace("\"", "");
            while (!File.Exists(videoPath))
            {
                Console.WriteLine("Please Enter Valid File path : ");
                videoPath = Console.ReadLine();
            }
        }


        private static string GenerateProxyVideo(string videoPath)
        {
            Console.WriteLine("[Content Moderator] Video compression process started...");
            var compressedVideoPath = FFmpegComponent.CompressVideo(videoPath);
            if (string.IsNullOrWhiteSpace(compressedVideoPath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[Content Moderator] Video Compression failed.");
                Console.ForegroundColor = ConsoleColor.White;
            }
            Console.WriteLine("[Content Moderator] Video compression process completed...");

            return compressedVideoPath;
        }

        private static async Task<VisualModerationResult> ProcessVideoModeration(string compressedVideoPath)
        {
            Console.WriteLine("[Content Moderator] Video moderation process started...");
            VisualModerationResult result = await visualModerator.ModerateVideo(compressedVideoPath);
            if (result == null)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[Content Moderator] Video moderation process failed.");
                Console.ForegroundColor = ConsoleColor.White;
            }

            Console.WriteLine("[Content Moderator] Video moderation process completed...");
            return result;
        }

        private static async Task<TextModerationResult> ProcessTextModeration(string webVttUrl)
        {
            Console.WriteLine("[Content Moderator] Text moderation process started...");
            TextModerationResult result = new TextModerationResult();
            try
            {
                string webVttString = await WebVttParser.LoadWebVtt(webVttUrl);
                result.webVtt = webVttString;
                List<CaptionTextModerationResult> captionTextResults = WebVttParser.ParseWebVtt(webVttString);
                result.captionTextResults = await textModerator.TextScreen(captionTextResults);
            }
            catch (Exception e)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[Content Moderator] Text moderation process failed. Error: ", e.ToString());
                Console.ForegroundColor = ConsoleColor.White;
            }

            Console.WriteLine("[Content Moderator] Text moderation process completed...");
            return result;
        }

        private static async Task<string> UploadVideoReview(VisualModerationResult vmResult, TextModerationResult tmResult)
        {
            Console.WriteLine("[Content Moderator] Video Review publishing started...");
            string reviewId = string.Empty;
            string videoFilePath = vmResult.VideoFilePath;

            try
            {
                reviewId = await videoReviewUploader.CreateVideoReview(vmResult, tmResult);
                await videoReviewUploader.AddVideoFramesToVideoReview(vmResult, reviewId);
                await videoReviewUploader.AddVideoTranscriptToVideoReview(tmResult, reviewId);
                await videoReviewUploader.AddVideoTranscriptModerationResultToVideoReview(tmResult, reviewId);
                await videoReviewUploader.PublishVideoReview(reviewId);
            }
            catch(Exception e)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[Content Moderator] Video Review publishing failed. Error: ", e.ToString());
                Console.ForegroundColor = ConsoleColor.White;
            }

            FFmpegComponent.CleanUp(videoFilePath, reviewId);
            Console.WriteLine("[Content Moderator] Video Review published Successfully and the review Id {0}", reviewId);

            return reviewId;
        }

    }
}
