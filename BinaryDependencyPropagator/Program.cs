using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BinaryDependencyPropagator
{
    /// <summary>
    /// # Binary Dependency Propagator
    /// (for .net developers)
    /// 
    /// [![Build Status](https://dev.azure.com/krzysztofjaniszewski0334/BinaryDependencyPropagator/_apis/build/status/krzyszsz.BinaryDependencyPropagator?branchName=master)](https://dev.azure.com/krzysztofjaniszewski0334/BinaryDependencyPropagator/_build/latest?definitionId=1&amp;branchName=master)
    /// 
    /// 
    /// ## What is it:
    /// This tool is meant to copy dll files between projects to facilitate debugging of .net applications.
    /// It is useful when a project is composed of separate solutions requiring manual copying of binaries between the directories.
    /// For example, many projects publish their binaries into a private NuGet repository and at a later stage of the build process
    /// they download these binary dependencies in a different project.
    /// To make debugging easier, you can locally use this tool to override your dll-s with the other dll-s you build on your machine
    /// but including your own debugging symbols. This tool automates this process with the assumption that the dll-s with the newest
    /// modification date should override the older dll-s with the same name.
    /// 
    /// ## Usage:
    /// * Open the file BinaryDependencyPropagator\Program.cs.
    /// * Modify the directory names in SearchCriteria property to match your project directories.
    /// * Build it.
    /// * Run it every time after you build one of the projects of your application that you would like to propagate
    /// to the other parts of your project.
    /// 
    /// ## WARNING:
    /// Think twice if this tool is the right choice in your case. It overrides *.dll files so YOU WILL LOSE some of your *.dll-s as
    /// they will be overridden by the newer versions of these dll-s
    /// 
    /// ## WARNING-2:
    /// This tool has not been extensively tested; I only tried it with my projects and I can prove it works with the integration test.
    /// Having said that, it might not work with your project.
    /// 
    /// Think of it as a template for building your own solution of your own problem.
    /// It is meant to be simple to modify so it looks more like a single-file script rather than a normal application.
    /// </summary>
    public class Program
    {
        public const string FileExtension = ".dll";
        public const string FileExtensionSearchPattern = "*.dll";
        public const string DebugSymbolsExtension = ".pdb";
        public const string NugetDirectory = "/packages/";
        public static Func<string, bool> CanBeSourceOfPropagation = x => !x.ToLower().Replace("\\", "/").Contains(NugetDirectory);
        public static Func<string, bool> CanBeDestinationOfPropagation = x => true; // Every dll can be overriden.

        public static FileSearchCriteria[] SearchCriteria =
        {
            new FileSearchCriteria {Root = @"c:\abc\cde"}, // Please specify all your source directories here.
            new FileSearchCriteria {Root = @"c:\abc\xyz"},
            new FileSearchCriteria {Root = @"c:\abc\aabbb"}
        };

        public static void Main()
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();
            var filesToLookInto = new FileSearch().GetFiles(SearchCriteria);
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
                        Console.WriteLine("Sleep before final attempt of action: {0}.", loggingMessage);
                        Thread.Sleep(TimeSpan.FromSeconds(1));
                    }

                    Retry(action, retriesLeft - 1, loggingMessage);
                }

                if (retriesLeft == 0)
                {
                    Console.WriteLine("Error '{0}' while attempting: {1}", e.Message, loggingMessage);
                }
            }
        }

        private T Retry<T>(Func<T> func, int retriesLeft = 3, string loggingMessage = null)
        {
            T result = default(T);
            Retry(() => { result = func(); }, retriesLeft, loggingMessage);
            return result;
        }

        public IEnumerable<string> GetFiles(string root)
        {
            return Retry(() => Directory.GetFiles(root, Program.FileExtensionSearchPattern, SearchOption.AllDirectories),
                loggingMessage: string.Format("GetFiles({0})", root));
        }

        public void CopyNewerFilesWithPdb(ICollection<FileData> files)
        {
            if (files.Count <= 1)
            {
                return;
            }

            var allFilesGroupedByName = files.ToLookup(x => Path.GetFileName(x.FullName));

            var subsetNotFromNuget = files.AsParallel().Where(x => Program.CanBeSourceOfPropagation(x.FullName));
            var subsetWithPdb = subsetNotFromNuget
                .Where(x => x.FullName.ToLower().EndsWith(Program.FileExtension) && File.Exists(ToPdb(x.FullName))).ToList();
            var srcCollection = subsetWithPdb.GroupBy(x => Path.GetFileName(x.FullName))
                .Select(x => new {itself = x, newest = x.Max(y => y.Date)}).Select(x => x.itself.First(y => y.Date == x.newest)).ToList();
            int processedFiles = 0;
            Parallel.ForEach(srcCollection, srcFile =>
            {
                var destinationFiles = allFilesGroupedByName[Path.GetFileName(srcFile.FullName)]
                    .Where(
                        x => x.FullName != srcFile.FullName &&
                        srcFile.Date > x.Date &&
                        Program.CanBeDestinationOfPropagation(x.FullName))
                    .Distinct();

                foreach (var destFile in destinationFiles)
                {
                    if (processedFiles % 10 == 0)
                    {
                        Console.Write(".");
                    }

                    Retry(() =>
                    {
                        if (File.Exists(destFile.FullName))
                            File.Delete(destFile.FullName);
                    }, loggingMessage: string.Format("delete:{0}", destFile.FullName));
                    Retry(() => File.Copy(srcFile.FullName, destFile.FullName),
                        loggingMessage: string.Format("copy:{0}=>{1}", srcFile.FullName, destFile.FullName));

                    var pdbSrc = ToPdb(srcFile.FullName);
                    var pdbDestination = ToPdb(destFile.FullName);
                    Retry(() =>
                    {
                        if (File.Exists(pdbDestination))
                            File.Delete(pdbDestination);
                    }, loggingMessage: string.Format("delete:{0}", pdbDestination));
                    Retry(() => File.Copy(pdbSrc, pdbDestination), loggingMessage: string.Format("copy:{0}=>{1}", pdbSrc, pdbDestination));

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

    public class FileSearch
    {
        public IList<FileData> GetFiles(IEnumerable<FileSearchCriteria> dllSearchCriteria)
        {
            var fileSystem = new FileSystem();
            var fileNames = dllSearchCriteria.AsParallel()
                .SelectMany(search => fileSystem.GetFiles(search.Root)
                .Where(x => search.IsAcceptable?.Invoke(x) ?? true));
            return fileNames.Select(x => new FileData { Date = File.GetLastWriteTimeUtc(x), FullName = x }).Distinct().ToList();
        }
    }
}
