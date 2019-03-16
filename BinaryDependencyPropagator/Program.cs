using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BinaryDependencyPropagator
{
    class Program
    {
        public const string FileExtension = ".dll";
        public const string FileExtensionSearchPattern = "*.dll";
        public const string DebugSymbolsExtension = ".pdb";
        public const string NugetDirectory = "/packages/";

        static void Main(string[] args)
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();
            var filesToLookInto = new FilesMappingGenerator().GetFileMap(new List<FileSearchCriteria>()
            {
                new FileSearchCriteria { Root = @"c:\abc\cde" },
                new FileSearchCriteria { Root = @"c:\abc\xyz" }
            });

            new FileSystem().CopyNewerFilesWithPdb(filesToLookInto);
            Console.WriteLine("Time: {0}s", sw.Elapsed.TotalSeconds);
        }
    }

    public class FileSystem
    {
        private void Retry(Action action, int retriesLeft = 3, string loggingMessage = null)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                if (retriesLeft > 0)
                {
                    if (retriesLeft == 1)
                    {
                        Console.WriteLine("Sleep before final attempt.");
                        Thread.Sleep(TimeSpan.FromSeconds(0.1));
                    }

                    Retry(action, retriesLeft - 1, loggingMessage);
                }

                if (retriesLeft == 0)
                {
                    Console.WriteLine("Err '{0}' while attempting: {1}", e.Message, loggingMessage);
                }
            }
        }

        public IEnumerable<string> GetFiles(string root)
        {
            return Directory.GetFiles(root, Program.FileExtensionSearchPattern, SearchOption.AllDirectories);
        }

        public void CopyNewerFilesWithPdb(ICollection<FileData> files)
        {
            if (files.Count <= 1)
            {
                return;
            }

            var allFilesGroupedByName = files.ToLookup(x => Path.GetFileName(x.FullName));

            var subsetNotFromNuget = files.Where(x => x.FullName.ToLower().Replace("\\", "/").Contains(Program.NugetDirectory));
            var subsetWithPdb = subsetNotFromNuget.Where(x => x.FullName.ToLower().EndsWith(Program.FileExtension) && File.Exists(x.FullName.Substring(x.FullName.Length - 4) + Program.DebugSymbolsExtension)).ToList();
            var srcCollection = subsetWithPdb.GroupBy(x => Path.GetFileName(x.FullName)).Select(x => new {itself = x, newest = x.Max(y => y.Date)}).Select(x => x.itself.First(y => y.Date == x.newest)).ToList();
            int processedFiles = 0;
            Parallel.ForEach(srcCollection, srcFile =>
            {
                var destinationFiles = allFilesGroupedByName[Path.GetFileName(srcFile.FullName)]
                    .Where(x => x.FullName != srcFile.FullName && srcFile.Date > x.Date).Distinct();

                foreach (var destFile in destinationFiles)
                {
                    if (processedFiles % 10 == 0)
                    {
                        Console.Write(".");
                    }

                    Retry(() => File.Delete(destFile.FullName), loggingMessage: string.Format("delete:{0}", destFile.FullName));
                    Retry(() => File.Copy(srcFile.FullName, destFile.FullName), loggingMessage: string.Format("copy:{0}=>{1}", srcFile.FullName, destFile.FullName));

                    var pdbSrc = ToPdb(srcFile.FullName);
                    var pdbDestination = ToPdb(destFile.FullName);
                    Retry(() => File.Copy(pdbSrc, pdbDestination), loggingMessage: string.Format("copy:{0}=>{1}", pdbSrc, pdbDestination));
                    Retry(() => File.Delete(pdbDestination), loggingMessage: string.Format("delete:{0}", pdbDestination));

                    Interlocked.Increment(ref processedFiles);
                }
            });

            Console.WriteLine();
            Console.WriteLine("Processed files: {0}", processedFiles);
        }

        private static string ToPdb(string fileName)
        {
            return fileName.Substring(0, fileName.Length - 4) + Program.DebugSymbolsExtension;
        }
    }

    public class FileSearchCriteria
    {
        public string Root { get; set; }
        public Func<string, bool> IsAcceptable { get; set; }
    }

    public struct FileData
    {
        public string FullName { get; set; }
        public DateTime Date { get; set; }
    }

    public class FilesMappingGenerator
    {
        public IList<FileData> GetFileMap(IList<FileSearchCriteria> fileMappingSearchCriteria)
        {
            var fileSystem = new FileSystem();
            var fileNames = fileMappingSearchCriteria.AsParallel()
                .SelectMany(fileMappingSearchCriterion => fileSystem.GetFiles(fileMappingSearchCriterion.Root)
                .Where(x => fileMappingSearchCriterion.IsAcceptable?.Invoke(x) ?? true));
            return fileNames.Select(x => new FileData { Date = File.GetLastWriteTimeUtc(x), FullName = x }).ToList();
        }
    }
}
