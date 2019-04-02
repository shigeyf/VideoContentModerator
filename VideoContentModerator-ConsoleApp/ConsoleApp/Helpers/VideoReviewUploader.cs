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

        public async Task<string> CreateVideoReview(VisualModerationResult vmResult, TextModerationResult tmResult)
        {
            // Step 1 - Create Video Review

            string reviewId = string.Empty;
            List<FrameEvent> frameEventList = ExtractFrameEventListFromJson(vmResult.VisualModeratedJson);
            frameEventList = MergeTextModerationResultToFrameEventList(frameEventList, tmResult.captionTextResults);

            reviewId = await CreateVideoReviewWithContentModeratorReviewAPI(vmResult, frameEventList);
            this.frameEventList = FixFrameNameInFrameEventList(frameEventList, reviewId);

            return reviewId;
        }

        public async Task<string> AddVideoFramesToVideoReview(VisualModerationResult result, string reviewId)
        {
            // Step 2 (a) - Add Video Frames to Video Review

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

        public async Task<string> AddVideoTranscriptToVideoReview(TextModerationResult result, string reviewId)
        {
            // Step 2 (b) - Add video transcript to Video Review

            bool isSuccess = true;

            isSuccess = await AddVideoTranscriptToReviewWithContentModeratorReviewAPI(reviewId, result.webVtt);
            if (!isSuccess)
            {
                throw new Exception("AddVideoTranscriptToReviewWithContentModeratorReviewAPI call failed.");
            }

            return reviewId;
        }

        public async Task<string> AddVideoTranscriptModerationResultToVideoReview(TextModerationResult result, string reviewId)
        {
            // Step 2 (c) - Add video transcript moderation result to Video Review

            bool isSuccess = true;

            isSuccess = await AddVideoTranscriptModerationResultToReviewWithContentModeratorReviewAPI(reviewId, result.captionTextResults);
            if (!isSuccess)
            {
                throw new Exception("AddVideoTranscriptModerationResultToReviewWithContentModeratorReviewAPI call failed.");
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

        public async Task<string> CreateVideoReviewWithContentModeratorReviewAPI(VisualModerationResult vmResult, List<FrameEvent> frameEventList)
        {
            string reviewId = string.Empty;
            try
            {
                string videoReviewsJson = CreateVideoReviewObject(vmResult, frameEventList);
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

        private string CreateVideoReviewObject(VisualModerationResult vmResult, List<FrameEvent> frameEventList)
        {
            List<VideoReview> videoReviews = GenerateVideoReviewList(vmResult, frameEventList);
            return JsonConvert.SerializeObject(videoReviews);
        }

        private List<VideoReview> GenerateVideoReviewList(VisualModerationResult vmResult, List<FrameEvent> frameEventList)
        {
            List<VideoReview> videoReviewList = new List<VideoReview>();
            VideoReview videoReviewObj = new VideoReview();

            videoReviewObj.Type = VideoEntityType;
            videoReviewObj.Content = vmResult.StreamingUrlDetails.SmoothUri;
            videoReviewObj.ContentId = vmResult.VideoName;
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
            var isAdult = double.Parse(adultScore) > reviewToolConfig.AdultFrameThreshold ? true : false;
            var racyScore = frameEventList.OrderByDescending(a => Double.Parse(a.RacyScore)).FirstOrDefault().RacyScore;
            var isRacy = double.Parse(racyScore) > reviewToolConfig.RacyFrameThreshold ? true : false;

            var adultTextScore = frameEventList.OrderByDescending(a => Double.Parse(a.AdultTextScore)).FirstOrDefault().AdultTextScore;
            var isAdultText = double.Parse(adultTextScore) > reviewToolConfig.Category1TextThreshold ? true : false;
            var racyTextScore = frameEventList.OrderByDescending(a => Double.Parse(a.RacyTextScore)).FirstOrDefault().RacyTextScore;
            var isRacyText = double.Parse(racyTextScore) > reviewToolConfig.Category2TextThreshold ? true : false;
            var offensiveTextScore = frameEventList.OrderByDescending(a => Double.Parse(a.OffensiveTextScore)).FirstOrDefault().OffensiveTextScore;
            var isOffensiveText = double.Parse(offensiveTextScore) > reviewToolConfig.Category3TextThreshold ? true : false;

            var reviewRecommended = frameEventList.Any(frame => frame.ReviewRecommended);
            metadata = new List<Metadata>()
            {
                new Metadata() { Key = "ReviewRecommended", Value = reviewRecommended.ToString() },
                new Metadata() { Key = "AdultScore", Value = adultScore },
                new Metadata() { Key = "a", Value = isAdult.ToString() },
                new Metadata() { Key = "RacyScore", Value = racyScore },
                new Metadata() { Key = "r", Value = isRacy.ToString() },
                new Metadata() { Key = "Category1TextScore", Value = adultTextScore },
                new Metadata() { Key = "at", Value = isAdultText.ToString() },
                new Metadata() { Key = "Category2TextScore", Value = racyTextScore },
                new Metadata() { Key = "rt", Value = isRacyText.ToString() },
                new Metadata() { Key = "Category3TextScore", Value = offensiveTextScore },
                new Metadata() { Key = "ot", Value = isOffensiveText.ToString() }
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
                        new Metadata() {Key = "at", Value = frameEvent.IsAdultTextContent.ToString() },
                        new Metadata() {Key = "rt", Value = frameEvent.IsRacyTextContent.ToString() },
                        new Metadata() {Key = "ot", Value = frameEvent.IsOffensiveTextContent.ToString() }

                    },
                    ReviewerResultTags = new List<ReviewResultTag>(),
                };
                videoFrames.Add(videoFrameObj);
            }
            return videoFrames;
        }

        #endregion


        #region ### Add Video Transcript operations

        public async Task<bool> AddVideoTranscriptToReviewWithContentModeratorReviewAPI(string reviewId, string webVtt)
        {
            bool isSuccess = false;
            try
            {
                byte[] webVttBytes = Encoding.UTF8.GetBytes(webVtt);
                var response = await this.client.Reviews.AddVideoTranscriptWithHttpMessagesAsync(this.reviewToolConfig.TeamId, reviewId, new MemoryStream(webVttBytes));
                isSuccess = response.Response.IsSuccessStatusCode;
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                throw;
            }
            return isSuccess;
        }

        #endregion


        #region ### Add Video Transcript Moderation Result operations

        public async Task<bool> AddVideoTranscriptModerationResultToReviewWithContentModeratorReviewAPI(string reviewId, List<CaptionTextModerationResult> captionTextModerationResultList)
        {
            bool isSuccess = false;
            try
            {
                string trascriptModerationResultListJson = CreateVideoTranscriptModerationResultObject(captionTextModerationResultList);
                List<TranscriptModerationBodyItem> trascriptModerationResultList = JsonConvert.DeserializeObject<List<TranscriptModerationBodyItem>>(trascriptModerationResultListJson);
                var response = await this.client.Reviews.AddVideoTranscriptModerationResultWithHttpMessagesAsync("application/json", reviewToolConfig.TeamId, reviewId, trascriptModerationResultList);
                isSuccess = response.Response.IsSuccessStatusCode;
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                throw;
            }
            return isSuccess;
        }

        private string CreateVideoTranscriptModerationResultObject(List<CaptionTextModerationResult> captionTextModerationResultList)
        {
            List<VideoTranscriptModerationResult> videoTranscriptionModerationResult = GeneratevideoTranscriptionModerationResult(captionTextModerationResultList);
            return JsonConvert.SerializeObject(videoTranscriptionModerationResult);
        }

        private List<VideoTranscriptModerationResult> GeneratevideoTranscriptionModerationResult(List<CaptionTextModerationResult> captionTextModerationResultList)
        {
            List<VideoTranscriptModerationResult> videoTrascriptModerationResultList = new List<VideoTranscriptModerationResult>();

            foreach (CaptionTextModerationResult captionTextModerationResult in captionTextModerationResultList)
            {
                if (captionTextModerationResult.ScreenResult.Terms != null)
                {
                    VideoTranscriptModerationResult videoTrascriptModerationResult = new VideoTranscriptModerationResult();
                    List<ModeratedTerm> terms = new List<ModeratedTerm>();
                    foreach (var term in captionTextModerationResult.ScreenResult.Terms)
                    {
                        ModeratedTerm termObj = new ModeratedTerm
                        {
                            Term = term.Term,
                            Index = term.OriginalIndex.Value
                        };
                        terms.Add(termObj);
                    }
                    videoTrascriptModerationResult.TimeStamp = "";
                    videoTrascriptModerationResult.Terms = terms;
                    videoTrascriptModerationResultList.Add(videoTrascriptModerationResult);
                }
            }

            return videoTrascriptModerationResultList;
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
                                eventObj.IsAdultTextContent = false;
                                eventObj.IsRacyTextContent = false;
                                eventObj.IsOffensiveTextContent = false;
                                eventObj.AdultTextScore = "0";
                                eventObj.RacyTextScore = "0";
                                eventObj.OffensiveTextScore = "0";
                                outputFrameEventsList.Add(eventObj);
                            }
                        }
                    }
                }
            }
            return outputFrameEventsList;
        }

        private List<FrameEvent> MergeTextModerationResultToFrameEventList(List<FrameEvent> frameEventList, List<CaptionTextModerationResult> captionTextResultList)
        {
            foreach (var captionTextResult in captionTextResultList)
            {
                foreach (var frame in frameEventList.Where(f => f.TimeStamp >= captionTextResult.StartTime && f.TimeStamp <= captionTextResult.EndTime))
                {
                    bool captionAdultTextTag = false;
                    bool captionRacyTextTag = false;
                    bool captionOffensiveTextTag = false;

                    double captionAdultTextScore = captionTextResult.ScreenResult.Classification.Category1.Score.Value;
                    double captionRacyTextScore = captionTextResult.ScreenResult.Classification.Category2.Score.Value;
                    double captionOffensiveTextScore = captionTextResult.ScreenResult.Classification.Category3.Score.Value;
                    if (captionTextResult.ScreenResult.Classification.Category1.Score.Value > reviewToolConfig.Category1TextThreshold) captionAdultTextTag = true;
                    if (captionTextResult.ScreenResult.Classification.Category2.Score.Value > reviewToolConfig.Category2TextThreshold) captionRacyTextTag = true;
                    if (captionTextResult.ScreenResult.Classification.Category3.Score.Value > reviewToolConfig.Category3TextThreshold) captionOffensiveTextTag = true;

                    frame.IsAdultTextContent = captionAdultTextTag ? captionAdultTextTag : frame.IsAdultTextContent;
                    frame.IsRacyTextContent = captionRacyTextTag ? captionRacyTextTag : frame.IsRacyTextContent;
                    frame.IsOffensiveTextContent = captionOffensiveTextTag ? captionOffensiveTextTag : frame.IsOffensiveTextContent;
                    frame.AdultTextScore = (captionAdultTextScore > Double.Parse(frame.AdultTextScore)) ? captionAdultTextScore.ToString() : frame.AdultTextScore;
                    frame.RacyTextScore = (captionRacyTextScore > Double.Parse(frame.RacyTextScore)) ? captionRacyTextScore.ToString() : frame.RacyTextScore;
                    frame.OffensiveTextScore = (captionOffensiveTextScore > Double.Parse(frame.OffensiveTextScore)) ? captionOffensiveTextScore.ToString() : frame.OffensiveTextScore;
                }
            }
            return frameEventList;
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
