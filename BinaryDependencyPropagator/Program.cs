using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BinaryDependencyPropagator
{
    class Program
    {
        static void Main(string[] args)
        {
            var filesToLookInto = new FilesMappingGenerator().GetFileMap(new List<FileSearchCriteria>()
            {
                new FileSearchCriteria { Root = @"c:\abc\cde" },
                new FileSearchCriteria { Root = @"c:\abc\xyz" }
            });

            new FileSystem().CopyNewerFileWithPdb(filesToLookInto);
        }
    }

    public class FileSystem
    {
        private void Retry(Action action, int retriesLeft = 3, bool failOnFinalAttempt = false)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);

                if (retriesLeft > 0)
                {
                    if (retriesLeft == 1)
                    {
                        Console.WriteLine("Sleep before final attempt.");
                        Thread.Sleep(TimeSpan.FromSeconds(0.1));
                    }

                    Retry(action, retriesLeft - 1, failOnFinalAttempt);
                }

                if (retriesLeft == 0 && failOnFinalAttempt)
                {
                    throw;
                }
            }
        }

        public IEnumerable<string> GetFiles(string root)
        {
            return Directory.GetFiles(root, "*.dll", SearchOption.AllDirectories);
        }

        public void CopyNewerFileWithPdb(ICollection<FileData> files)
        {
            if (files.Count <= 1)
            {
                return;
            }

            var subsetNotFromNuget = files.Where(x => x.FullName.Replace("\\", "/").Contains($"/packages/"));
            var subsetWithPdb = subsetNotFromNuget.Where(x => x.FullName.EndsWith(".dll") && File.Exists(x.FullName.Substring(x.FullName.Length - 4) + ".pdb")).ToList();
            var newest = subsetWithPdb.Max(x => x.Date);
            var src = subsetWithPdb.First(x => x.Date == newest);

            var destCollection = files.Where(x => x.FullName.EndsWith(".dll")).Except(new []{ src }).ToList();
            Parallel.ForEach(destCollection, destFile =>
            {
                Retry(() => File.Copy(src.FullName, destFile.FullName));
                Retry(() => File.Copy(src.FullName.Substring(src.FullName.Length - 4) + ".pdb", Path.GetDirectoryName(destFile.FullName) ?? string.Empty));
            });
        }
    }

    public class FileSearchCriteria
    {
        public string Root { get; set; }
        public Func<string, bool> IsAcceptable { get; set; }
    }

    public class FileData
    {
        public string FullName { get; set; }
        public DateTime Date { get; set; }
    }

    public class FilesMappingGenerator
    {
        public IList<FileData> GetFileMap(IList<FileSearchCriteria> fileMappingSearchCriteria)
        {
            var fileSystem = new FileSystem();
            var fileNames = fileMappingSearchCriteria
                .SelectMany(fileMappingSearchCriterion => fileSystem.GetFiles(fileMappingSearchCriterion.Root)
                .Where(x => fileMappingSearchCriterion.IsAcceptable?.Invoke(x) ?? true));
            return fileNames.Select(x => new FileData { Date = File.GetLastWriteTimeUtc(x), FullName = x }).ToList();
        }
    }
}
