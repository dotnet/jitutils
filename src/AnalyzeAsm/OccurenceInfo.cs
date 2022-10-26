// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using MethodDB = System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<AnalyzeAsm.PositionInfo>>>;

namespace AnalyzeAsm
{
    /// <summary>
    ///     Represents the positions in file where the method is present.
    /// </summary>
    public class PositionInfo
    {
        public int s;
        public int l;

        public PositionInfo(int start, int length)
        {
            this.s = start;
            this.l = length;
        }

        public override string ToString()
        {
            return $"{s} : {l}";
        }
    }

    /// <summary>
    ///     Index file that is serialized by the indexer.
    /// </summary>
    public class MethodIndex
    {
        public DateTime LastModifiedTimeOfExe = DateTime.MinValue;
        public Dictionary<string, DateTime> LastModifiedTime = null;
        public MethodDB MethodDatabase = null;
        public static Dictionary<string, List<PositionInfo>> EmptyEntries = new Dictionary<string, List<PositionInfo>>();

        public MethodIndex(DateTime lastModifiedTimeOfExe, Dictionary<string, DateTime> modifiedTimeInfo, MethodDB methodsInfo)
        {
            LastModifiedTimeOfExe = lastModifiedTimeOfExe;
            LastModifiedTime = modifiedTimeInfo;
            MethodDatabase = methodsInfo;
        }

        /// <summary>
        ///     Search for occurences of <paramref name="methodName"/>.
        /// </summary>
        /// <param name="methodName"></param>
        /// <returns></returns>
        internal Dictionary<string, List<PositionInfo>> GetOccurences(string methodName)
        {
            if (MethodDatabase.TryGetValue(methodName, out Dictionary<string, List<PositionInfo>> result))
            {
                return result;
            }
            return EmptyEntries;
        }

        /// <summary>
        ///     Process each .dasm file and find positions for every assembly listing.
        /// </summary>
        /// <param name="fileName"></param>
        internal void AddFile(string fileName)
        {
            LastModifiedTime[Path.GetFileName(fileName)] = File.GetLastWriteTimeUtc(fileName);

            string line, methodName = null;
            int lineNumber = 1, startLineNumber = 0;
            using FileStream fs = File.Open(fileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using BufferedStream bs = new BufferedStream(fs);
            using StreamReader sr = new StreamReader(bs);
            while ((line = sr.ReadLine()) != null)
            {
                if (line.StartsWith("; Assembly listing for method"))
                {
                    if (startLineNumber > 0)
                    {
                        if (!MethodDatabase.TryGetValue(methodName, out Dictionary<string, List<PositionInfo>> fileList))
                        {
                            fileList = new Dictionary<string, List<PositionInfo>>();
                            MethodDatabase[methodName] = fileList;
                        }
                        if (!fileList.TryGetValue(fileName, out List<PositionInfo> positions))
                        {
                            positions = new List<PositionInfo>();
                            fileList[fileName] = positions;
                        }

                        MethodDatabase[methodName][fileName].Add(new PositionInfo(startLineNumber, lineNumber - startLineNumber));
                    }

                    methodName = line.Replace("; Assembly listing for method ", string.Empty);
                    startLineNumber = lineNumber;
                }
                lineNumber++;
            }
        }
    }

    /// <summary>
    ///     Indexer that creates .index file for all methods and their timestamps.
    /// </summary>
    internal class Indexer
    {
        private static Stopwatch stopWatch = new Stopwatch();
        private static readonly string indexFileName = ".index";

        /// <summary>
        ///     Creates .index file based on .dasm files present in <paramref name="folderPath"/>.
        /// </summary>
        /// <param name="folderPath"></param>
        private static MethodIndex CreateIndex(string folderPath)
        {
            if (!Directory.Exists(folderPath))
            {
                throw new DirectoryNotFoundException($"Direction {folderPath} not found.");
            }

            stopWatch.Restart();

            var timestamps = new Dictionary<string, DateTime>();
            var methodDatabase = new MethodDB();
            MethodIndex index = new MethodIndex(File.GetLastWriteTimeUtc(Assembly.GetExecutingAssembly().Location), timestamps, methodDatabase);

            var dasmFiles = Directory.GetFiles(folderPath, "*.dasm", SearchOption.TopDirectoryOnly);
            foreach (var dasmFile in dasmFiles)
            {
                index.AddFile(dasmFile);
            }

            string contents = JsonConvert.SerializeObject(index);
            File.WriteAllText(Path.Combine(folderPath, indexFileName), contents);

            stopWatch.Stop();
            Console.WriteLine($"Index created in {stopWatch.Elapsed.TotalSeconds} secs.");
            return index;
        }

        /// <summary>
        ///     Checks if .index file should be created or not.
        /// </summary>
        /// <param name="folderPath"></param>
        /// <param name="doExeTimeStampCheck">Should exe timestamp check done or not.</param>
        private static bool TryGetIndex(string folderPath, out MethodIndex index, bool doExeTimeStampCheck = true)
        {
            index = null;
            string indexFilePath = Path.Combine(folderPath, indexFileName);

            if (!File.Exists(indexFilePath))
            {
                return false;
            }

            index = JsonConvert.DeserializeObject<MethodIndex>(File.ReadAllText(indexFilePath));
            var expectedExeTimeStamp = index.LastModifiedTimeOfExe;
            var actualExeTimeStamp = File.GetLastWriteTimeUtc(Assembly.GetExecutingAssembly().Location);

            // If exe timestamp check should be done
            if (doExeTimeStampCheck && expectedExeTimeStamp != actualExeTimeStamp)
            {
                return false;
            }

            var timestamps = index.LastModifiedTime;
            foreach (var fileEntry in timestamps)
            {
                string fullFilePath = Path.Combine(folderPath, fileEntry.Key);

                if (!File.Exists(fullFilePath) || fileEntry.Value != File.GetLastWriteTimeUtc(fullFilePath))
                {
                    // recreate index if dasm files timestamp don't match
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        ///     Loads the index metadata and verifies the timestamps of .dasm files as well as exe.
        ///     If they match, just returns otherwise creates .index file.
        /// </summary>
        /// <param name="folderPath"></param>
        /// <param name="doExeTimeStampCheck">If timestamp check of exe to be done.</param>
        public static MethodIndex GetIndex(string folderPath, bool doExeTimeStampCheck = false)
        {
            MethodIndex index;
            if (!TryGetIndex(folderPath, out index, doExeTimeStampCheck))
            {
                index = CreateIndex(folderPath);
            }
            return index;
        }
    }
}
