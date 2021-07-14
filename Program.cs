using System;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Collections.Generic;

namespace Waifu2x_caffe_video
{
    class Program
    {
        public static int scaleRate = 2;

        public static int extractCount = 50;
        public static int needExtractCount = 20;

        public class CaffeProcess
        {
            public Process process;
            public int inx;
        }
        public struct CaffeProcessInfo
        {
            public int GPUId;
            public int SplitSize;
            public int BatchSize;
        }

        private static ProcessStartInfo GenericProcessStartInfo()
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.WindowStyle = ProcessWindowStyle.Hidden;
            startInfo.UseShellExecute = false;
            startInfo.RedirectStandardError = true;
            startInfo.RedirectStandardOutput = true;

            return startInfo;
        }

        private static void StartProcessWithNoRedirect(Process process)
        {
            process.OutputDataReceived += Process_OutputDataReceived;
            process.ErrorDataReceived += Process_OutputDataReceived;
            process.Start();
            process.BeginErrorReadLine();
            process.BeginOutputReadLine();
        }

        private static void Process_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if(e.Data == null || e.Data.Trim() == "")
            {
                return;
            }
            Debug.WriteLine(e.Data);
        }

        private static void ExtractFrameCount(string path)
        {
            Process process = new Process();
            ProcessStartInfo startInfo = GenericProcessStartInfo();
            startInfo.FileName = "cmd.exe";
            startInfo.Arguments = "/C ffprobe -v error -select_streams v:0 -count_packets -show_entries stream=nb_read_packets -of csv=p=0 \"" + path + "\" > fc.txt";
            process.StartInfo = startInfo;
            StartProcessWithNoRedirect(process);
            process.WaitForExit();
        }

        private static void ExtractFrameRate(string path)
        {
            Process process = new Process();
            ProcessStartInfo startInfo = GenericProcessStartInfo();
            startInfo.FileName = "cmd.exe";
            startInfo.Arguments = "/C ffprobe -v error -select_streams v -of default=noprint_wrappers=1:nokey=1 -show_entries stream=r_frame_rate \"" + path + "\" > fr.txt";
            process.StartInfo = startInfo;

            StartProcessWithNoRedirect(process);
            process.WaitForExit();
        }

        private static void ExtractVideoSize(string path)
        {
            Process process = new Process();
            ProcessStartInfo startInfo = GenericProcessStartInfo();
            startInfo.FileName = "cmd.exe";
            startInfo.Arguments = $"/C ffprobe -v error -select_streams v -show_entries stream=width,height -of csv=p=0:s=x \"{path}\" > size.txt";
            process.StartInfo = startInfo;
            StartProcessWithNoRedirect(process);
            process.WaitForExit();                
        }

        private static List<int> ExtractFrame(string path, int request)
        {
            Process process = new Process();
            ProcessStartInfo startInfo = GenericProcessStartInfo();
            startInfo.FileName = "ffmpeg";

            int start = request;//(int) (Math.Ceiling((double) request / extractCount) * extractCount);

            startInfo.Arguments = $"-i \"{path}\" -vf select='between(n\\,{start}\\,{start+extractCount})' -frames:v {extractCount} -vsync 0 -start_number {start} Z:\\%d.bmp";
            process.StartInfo = startInfo;
            StartProcessWithNoRedirect(process);
            process.WaitForExit();

            List<int> result = new List<int>();

            for(int i = 0; i < extractCount; i++)
            {
                if(File.Exists(@$"Z:\{start+i}.bmp"))
                {
                    result.Add(start + i);
                }
                else
                {
                    break;
                }
            }

            return result;
        }

        private static CaffeProcess StartWorkFrame(string path, CaffeProcessInfo info, int inx)
        {
            Process process = new Process();
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.WindowStyle = ProcessWindowStyle.Hidden;
            startInfo.FileName = "waifu2x-caffe\\waifu2x-caffe-cui.exe";

            startInfo.Arguments = $"--gpu {info.GPUId} --model_dir \"waifu2x-caffe\\models\\anime_style_art\" -e bmp -b {info.BatchSize} -c {info.SplitSize} -p gpu -s {scaleRate}.0 -n 3 -m noise_scale -i Z:\\{inx}.bmp -o Z:\\{inx}-2x.bmp";
            process.StartInfo = startInfo;
            process.Start();

            return new CaffeProcess { inx = inx, process = process };
        }

        private static bool IsDone(CaffeProcess process)
        {
            return process.process.HasExited;
        }

        private static CaffeProcess ConvertResult(string framesFolder, int inx)
        {
            Process process = new Process();
            ProcessStartInfo startInfo = GenericProcessStartInfo();
            startInfo.FileName = "ffmpeg";
            startInfo.Arguments = $"-i Z:\\{inx}-2x.bmp \"{Path.Combine(framesFolder, $"{inx}.png")}\"";
            process.StartInfo = startInfo;
            StartProcessWithNoRedirect(process);
            return new CaffeProcess { inx = inx, process = process };
        }

        private static void ConvertFramesToVideo(int framerate, string path, string framesFolder, string outputPath)
        {
            Process process = new Process();
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = "ffmpeg";
            startInfo.Arguments = $"-framerate {framerate} -i \"{framesFolder}\\%d.png\" -i \"{path}\" -map 0:v -map 1:a -crf 17 -preset veryslow \"{outputPath}\"";
            process.StartInfo = startInfo;
            process.Start();
            process.WaitForExit();
        }

        private static bool CheckResultExist(string framesFolder, int inx)
        {
            return File.Exists(Path.Combine(framesFolder, $"{inx}.png"));
        }

        private static void DeleteCache(int inx)
        {
            if(File.Exists(@$"Z:\{inx}.bmp"))
            {
                File.Delete(@$"Z:\{inx}.bmp");
            }
        }

        public static void RewriteLine(string msg)
        {
            Console.SetCursorPosition(0, Console.CursorTop);
            Console.Write(msg);
            Console.Write(new string(' ', Console.WindowWidth - msg.Length));
        }

        static void Main(string[] args)
        {
            //Checklist
            //Z: mounted with fast-drive (I'm using ramdisk)
            //Video must cfr

            var caffeProcessInfo = new CaffeProcessInfo[] {
                new CaffeProcessInfo { GPUId = 0, SplitSize = 128, BatchSize = 4 },
                new CaffeProcessInfo { GPUId = 0, SplitSize = 128, BatchSize = 4 },
                new CaffeProcessInfo { GPUId = 0, SplitSize = 128, BatchSize = 4 },
                new CaffeProcessInfo { GPUId = 1, SplitSize = 128, BatchSize = 4 },
                new CaffeProcessInfo { GPUId = 1, SplitSize = 128, BatchSize = 4 },
                new CaffeProcessInfo { GPUId = 1, SplitSize = 128, BatchSize = 4 }
                //new CaffeProcessInfo { GPUId = 1, SplitSize = 128, BatchSize = 4 }
                };


            //Check waifu2x-caffe is exist
            if (!File.Exists("waifu2x-caffe\\waifu2x-caffe-cui.exe")) {
                Console.WriteLine("Need to install waifu2x-caffe.");
                Console.ReadLine();
                return;
            }

            //Check source is exist
            var path = args[0];
            if (!File.Exists(path))
            {
                Console.WriteLine("Not Exist: " + path);
                Console.ReadLine();
                return;
            }
            
            //Check user checked video is cfr
            if (!Path.GetFileNameWithoutExtension(path).EndsWith(".cfr"))
            {
                Console.WriteLine("File name(without extension) not ends with .cfr, Video is CFR? Type 'Yes' if right.");
                var yes = Console.ReadLine().Trim().ToLower();
                if (yes != "yes")
                {
                    Console.WriteLine("Please convert video to cfr first :(");
                    Console.ReadLine();
                    return;
                }
            }

            //Get FrameCount / FPS / Size
            ExtractFrameRate(path);
            ExtractFrameCount(path);
            ExtractVideoSize(path);

            var totalFrame = int.Parse(File.ReadAllText("fc.txt"));
            var frameRate = int.Parse(File.ReadAllText("fr.txt").Split("/")[0]);

            var size = File.ReadAllText("size.txt");
            var width = int.Parse(size.Split("x")[0]);
            var height = int.Parse(size.Split("x")[1]);

            var needSize = (width * height * 3 * extractCount) + ((width * 2) * (height * 2) * 3 * extractCount);

            
            Console.WriteLine($"Work file size: {width}x{height}" );
            Console.WriteLine($"Work file frame count: {totalFrame} / framerate: {frameRate}" );
            var minute = totalFrame * 3 / 60;
            var hour = Math.Floor(minute / 60d);
            Console.WriteLine($"Temp Drive Max Usage(usually use less): {needSize / 1024 / 1024}MB");
            Console.WriteLine($"If 1 frame per 3 second, Possible Process Time: {hour} Hour, {minute % 60} Minute");
            Console.WriteLine("Continue?");
            Console.ReadLine();

            var framesFolderPath = Path.Combine(Path.GetDirectoryName(path), Path.GetFileName(path) + "-frames");
            if (!Directory.Exists(framesFolderPath))
            {
                Directory.CreateDirectory(framesFolderPath);
            }
            else
            {
                Console.WriteLine("Frames folder already exist? continue process, if doesn't want it, remove -frames folder.");
            }

            var processList = new CaffeProcess[caffeProcessInfo.Length];
            for(int i = 0; i < processList.Length; i++)
            {
                processList[i] = null;
            }

            int doneCount = 0;

            Action ProcessPrint = delegate ()
            {
                RewriteLine($"{doneCount} / {totalFrame}");
            };            

            var extracted = new List<int>();
            var noMoreFrame = false;

            int request = 0;
            int completeCount = 0;

            for(int i = 0; i < totalFrame; i++)
            {
                if(!CheckResultExist(framesFolderPath,i))
                {
                    break;
                }
                doneCount += 1;
                completeCount = i+1;
            }
            ProcessPrint();
            request = (int) Math.Floor((double) completeCount / extractCount) * extractCount;

            var converts = new List<CaffeProcess>();

            for(int i = completeCount; i < totalFrame; i++)
            {
                converts.RemoveAll(item => { 
                    if (IsDone(item))
                    {
                        File.Delete($"Z:\\{item.inx}-2x.bmp");
                        return true;
                    }
                    return false;
                });
                if (!noMoreFrame && extracted.Count < needExtractCount)
                {
                    var result = ExtractFrame(path, request);
                    request += extractCount;
                    if (result.Count != extractCount)
                    {
                        noMoreFrame = true;
                    }
                    extracted.AddRange(result);
                }
                bool ordered = false;
                for(int pi = 0; pi < processList.Length; pi++)
                {
                    if ( processList[pi] == null )
                    {
                        processList[pi] = StartWorkFrame(path, caffeProcessInfo[pi], i);
                        ordered = true;
                        break;
                    }
                    else
                    {
                        if( IsDone(processList[pi]) )
                        {
                            var oldProcess = processList[pi];
                            processList[pi] = StartWorkFrame(path, caffeProcessInfo[pi], i);
                            doneCount++;
                            ProcessPrint();
                            ordered = true;

                            extracted.Remove(oldProcess.inx);
                            DeleteCache(oldProcess.inx);
                            converts.Add(ConvertResult(framesFolderPath, oldProcess.inx));
                            break;
                        }
                    }
                }
                if(!ordered)
                {
                    Thread.Sleep(1);
                    i--;
                }
            }

            while (true)
            {
                bool waitingProcess = false;
                for (int pi = 0; pi < processList.Length; pi++)
                {
                    if (processList[pi] == null) continue;
                    if (IsDone(processList[pi]))
                    {
                        processList[pi].process.WaitForExit();
                        var oldProcess = processList[pi];
                        processList[pi] = null;
                        doneCount++;
                        ProcessPrint();

                        extracted.Remove(oldProcess.inx);
                        DeleteCache(oldProcess.inx);
                        converts.Add(ConvertResult(framesFolderPath, oldProcess.inx));
                    }
                    waitingProcess = true;
                }
                if(!waitingProcess)
                {
                    break;
                }
            }

            while(true)
            {
                converts.RemoveAll(item => {
                    if (IsDone(item))
                    {
                        File.Delete($"Z:\\{item.inx}-2x.bmp");
                        return true;
                    }
                    return false;
                });
                if(converts.Count == 0)
                {
                    break;
                }
            }

            Console.WriteLine("Starting Convert frames to video");

            ConvertFramesToVideo(frameRate, path, framesFolderPath, path + "-waifu.mkv");

            Console.WriteLine("Please delete frames folder if result is ok, check video/waifu result");
        }
    }
}
