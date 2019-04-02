/*
 * Copyright (c) 2019
 * Released under the MIT license
 * http://opensource.org/licenses/mit-license.php
 */

using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;


namespace Microsoft.ContentModerator.VideoContentModerator
{
    public class FFmpegComponent
    {
        public static string _FFmpegExecutablePath = @"..\..\..\Lib\ffmpeg.exe";
        public static int _MaxCmdLineChars = 30000;
        public static string _WorkOutputDir = @"..\..\..\_temp\";

        //AmsConfigurations _configObj = null;
        public FFmpegComponent()
        {
        }

        public static string CompressVideo(string videoPath)
        {
            Console.WriteLine("Video Compression File: " + videoPath);
            Console.WriteLine("Video Compression with ffmpeg in-progress...");

            Initialize();

            string ffmpegBlobUrl;
            if (File.Exists(_FFmpegExecutablePath))
            {
                ffmpegBlobUrl = _FFmpegExecutablePath;
            }
            else
            {
                Console.WriteLine("ffmpeg.exe is missing. Please check the Lib folder");
                throw new Exception();
            }

            string compressedVideoFilePath = videoPath.Split('.')[0] + "_c.mp4";
            compressedVideoFilePath = _WorkOutputDir + Path.GetFileName(compressedVideoFilePath);
            ProcessStartInfo processStartInfo = new ProcessStartInfo();
            processStartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            processStartInfo.FileName = ffmpegBlobUrl;
            processStartInfo.Arguments = "-loglevel panic -hide_banner -i \"" + videoPath + "\" -vcodec libx264 -n -crf 32 -preset veryfast -vf scale=640:-1 -c:a aac -aq 1 -ac 2 -threads 0 \"" + compressedVideoFilePath + "\"";
            var process = Process.Start(processStartInfo);
            process.WaitForExit();
            process.Close();
            Console.WriteLine("Video Compression with ffmpeg done: " + compressedVideoFilePath);

            return compressedVideoFilePath;
        }

        public static string GenerateFrameImages(List<FFmpegVideoFrameImage> videoFrameImages, string id, int segments)
        {
            string frameStorageLocalPath = _WorkOutputDir + id;
            string frameZipFilesPath = frameStorageLocalPath + "_zip";

            Initialize();

            Directory.CreateDirectory(frameStorageLocalPath);
            for (int i = 0; i < segments; i++)
            {
                string dirPath = frameStorageLocalPath + "\\" + i;
                if (!Directory.Exists(dirPath))
                {
                    Directory.CreateDirectory(dirPath);
                }
            }

            List<string> args = new List<string>();
            int frameIndexInCmd = 0;
            StringBuilder sb = new StringBuilder();
            sb.Append("-loglevel panic -hide_banner ");
            foreach (var vfi in videoFrameImages)
            {
                vfi.FrameImageFilePath = frameStorageLocalPath + vfi.FrameImageFilePath;
                vfi.FrameImagesPath = frameStorageLocalPath + vfi.FrameImagesPath;

                var line = "-ss " + vfi.FramePosition + " -i \"" + vfi.VideoFilePath + "\" -map " + frameIndexInCmd + ":v -frames:v 1 -vf scale=320:-1 \"" + vfi.FrameImageFilePath + "\" ";
                frameIndexInCmd++;
                sb.Append(line);

                if (sb.Length > _MaxCmdLineChars)
                {
                    args.Add(sb.ToString());
                    sb.Clear();
                    sb.Append("-loglevel panic -hide_banner ");
                    frameIndexInCmd = 0;
                }
            }
            if (sb.Length != 0)
            {
                args.Add(sb.ToString());
            }

            Parallel.ForEach(args,
                new ParallelOptions { MaxDegreeOfParallelism = 4 },
                arg => CreateFrameImage(arg));

            Console.WriteLine(videoFrameImages.Count + " Frame Images were created successfully.");

            Directory.CreateDirectory(frameZipFilesPath);
            DirectoryInfo[] dirs = new DirectoryInfo(frameStorageLocalPath).GetDirectories();
            foreach (var dir in dirs)
            {
                ZipFile.CreateFromDirectory(dir.FullName, frameStorageLocalPath + $"_zip\\{dir.Name}.zip");
            }

            Console.WriteLine("Frame Image ZIP files were created successfully.");
            return frameZipFilesPath;
        }

        public static void CreateFrameImage(string arg)
        {
            Guid guid = Guid.NewGuid();
            Console.WriteLine("Video Frames Creation with ffmpeg ({0}) in-progress...", guid.ToString());

            Initialize();

            string ffmpegBlobUrl = string.Empty;
            if (File.Exists(_FFmpegExecutablePath))
            {
                ffmpegBlobUrl = _FFmpegExecutablePath;
            }
            else
            {
                Console.WriteLine("ffmpeg.exe is missing. Please check the Lib folder.");
                throw new Exception();
            }

            ProcessStartInfo processStartInfo = new ProcessStartInfo();
            processStartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            processStartInfo.FileName = ffmpegBlobUrl;
            processStartInfo.Arguments = arg;
            var process = Process.Start(processStartInfo);
            process.WaitForExit();
            process.Close();

            Console.WriteLine("Video Frames Creation with ffmpeg({0}) done.", guid.ToString());
        }

        public static void Initialize()
        {
            if (!Directory.Exists(_WorkOutputDir))
            {
                Directory.CreateDirectory(_WorkOutputDir);
            }
        }

        public static void CleanUp(string videoFilePath, string reviewId)
        {
            try
            {
                string frameImageFilesPath = _WorkOutputDir + reviewId;
                string frameImageZipFilesPath = _WorkOutputDir + reviewId + "_zip";
                Directory.Delete(frameImageFilesPath, true);
                FileInfo[] files = new DirectoryInfo(frameImageZipFilesPath).GetFiles();
                foreach (var file in files)
                {
                    File.Delete(file.FullName);
                }
                Directory.Delete(frameImageZipFilesPath, true);
                File.Delete(videoFilePath);
            }
            catch (Exception e)
            {
                Console.WriteLine("Cleanup failed: " + e.ToString());
                return;
            }
            Console.WriteLine("Cleaned up working files.");
        }
    }

    public class FFmpegVideoFrameImage
    {
        public string VideoFilePath { get; set; }
        public string FrameImagesPath { get; set; }
        public string FrameImageFilePath { get; set; }
        public TimeSpan FramePosition { get; set; }
    }

}
