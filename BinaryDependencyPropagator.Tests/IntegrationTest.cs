using System;
using System.IO;
using System.Linq;
using System.Threading;
using NUnit.Framework;

namespace BinaryDependencyPropagator.Tests
{
    [TestFixture]
    public class IntegrationTest
    {
        private const string SharedCoreDir = "SharedCore";
        private const string Child1Dir = "Child1";
        private const string Child2Dir = "Child2";
        private const string Child2SubDir = @"Child2\Sub";

        private string _testDirectory;

        [SetUp]
        public void SetUp()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "testDataCanBeRemovedIfLeft_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testDirectory);
            Console.WriteLine("Test directory: {0}", _testDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_testDirectory))
            {
                try
                {
                    Directory.Delete(_testDirectory, true);
                }
                catch (Exception)
                {
                    Thread.Sleep(TimeSpan.FromSeconds(1));  // Filesystems are not perfect, especially net fs, and you will not always get what you want (but what you need?).
                    Directory.Delete(_testDirectory, true);
                }
            }
        }

        [Test]
        public void RunIntegrationTest()
        {
            Arrange();
            Act();
            AssertMethod();
        }

        private void Arrange()
        {
            Program.SearchCriteria = new []
            {
                new FileSearchCriteria {Root = Path.Combine(_testDirectory, SharedCoreDir)},
                new FileSearchCriteria {Root = Path.Combine(_testDirectory, Child1Dir)},
                new FileSearchCriteria {Root = Path.Combine(_testDirectory, Child2Dir)},
            };

            Directory.CreateDirectory(Path.Combine(_testDirectory, SharedCoreDir));
            Directory.CreateDirectory(Path.Combine(_testDirectory, Child1Dir));
            Directory.CreateDirectory(Path.Combine(_testDirectory, Child2Dir));
            Directory.CreateDirectory(Path.Combine(_testDirectory, Child2SubDir));

            File.WriteAllText(Path.Combine(_testDirectory, SharedCoreDir, "file.dll"), "newModifiedFile_content");
            File.WriteAllText(Path.Combine(_testDirectory, SharedCoreDir, "file.pdb"), "newModifiedFile_pdb_content");
            File.WriteAllText(Path.Combine(_testDirectory, SharedCoreDir, "notUsedFile.dll"), "notUsedFile_content");
            File.WriteAllText(Path.Combine(_testDirectory, SharedCoreDir, "notUsedFile.pdb"), "notUsedFile_pdb_content");

            File.WriteAllText(Path.Combine(_testDirectory, "file.dll"), "outside modification tree");
            File.SetLastWriteTimeUtc(Path.Combine(_testDirectory, "file.dll"), DateTime.UtcNow.Add(TimeSpan.FromHours(-10)));

            File.WriteAllText(Path.Combine(_testDirectory, Child1Dir, "file.dll"), "old content 123");
            File.SetLastWriteTimeUtc(Path.Combine(_testDirectory, Child1Dir, "file.dll"), DateTime.UtcNow.Add(TimeSpan.FromHours(-10)));

            File.WriteAllText(Path.Combine(_testDirectory, Child1Dir, "file.pdb"), "old content pdb 123");
            File.SetLastWriteTimeUtc(Path.Combine(_testDirectory, Child1Dir, "file.pdb"), DateTime.UtcNow.Add(TimeSpan.FromHours(-10)));

            File.WriteAllText(Path.Combine(_testDirectory, Child2SubDir, "file.dll"), "old content 44");
            File.SetLastWriteTimeUtc(Path.Combine(_testDirectory, Child2SubDir, "file.dll"), DateTime.UtcNow.Add(TimeSpan.FromHours(-10)));

            File.WriteAllText(Path.Combine(_testDirectory, Child2SubDir, "notRelated.dll"), "something else");
            File.SetLastWriteTimeUtc(Path.Combine(_testDirectory, Child2SubDir, "notRelated.dll"), DateTime.UtcNow.Add(TimeSpan.FromHours(-10)));

            File.WriteAllText(Path.Combine(_testDirectory, Child2SubDir, "notRelated.txt"), "something else ttt");
            File.SetLastWriteTimeUtc(Path.Combine(_testDirectory, Child2SubDir, "notRelated.txt"), DateTime.UtcNow.Add(TimeSpan.FromHours(-10)));

            Thread.Sleep(TimeSpan.FromSeconds(1));  // Filesystems are not perfect, especially net fs, and you will not always get what you want (but what you need?).
        }

        private void Act()
        {
            Program.Main();
        }

        private void AssertMethod()
        {
            Assert.That(Directory.GetFiles(_testDirectory, "*.*", SearchOption.AllDirectories).OrderBy(x => x),
                Is.EquivalentTo(new []
                {
                    Path.Combine(_testDirectory, Child1Dir, "file.dll"),
                    Path.Combine(_testDirectory, Child1Dir, "file.pdb"),
                    Path.Combine(_testDirectory, Child2SubDir, "file.dll"),
                    Path.Combine(_testDirectory, Child2SubDir, "file.pdb"),
                    Path.Combine(_testDirectory, Child2SubDir, "notRelated.dll"),
                    Path.Combine(_testDirectory, Child2SubDir, "notRelated.txt"),
                    Path.Combine(_testDirectory, SharedCoreDir, "file.dll"),
                    Path.Combine(_testDirectory, SharedCoreDir, "file.pdb"),
                    Path.Combine(_testDirectory, SharedCoreDir, "notUsedFile.dll"),
                    Path.Combine(_testDirectory, SharedCoreDir, "notUsedFile.pdb"),
                    Path.Combine(_testDirectory, "file.dll"),
                }));

            Assert.That(File.ReadAllText(Path.Combine(_testDirectory, Child1Dir, "file.dll")),
                Is.EqualTo("newModifiedFile_content"));

            Assert.That(File.ReadAllText(Path.Combine(_testDirectory, Child1Dir, "file.pdb")),
                Is.EqualTo("newModifiedFile_pdb_content"));

            Assert.That(File.ReadAllText(Path.Combine(_testDirectory, Child2SubDir, "file.dll")),
                Is.EqualTo("newModifiedFile_content"));

            Assert.That(File.ReadAllText(Path.Combine(_testDirectory, Child2SubDir, "file.pdb")),
                Is.EqualTo("newModifiedFile_pdb_content"));

            Assert.That(File.ReadAllText(Path.Combine(_testDirectory, Child2SubDir, "notRelated.dll")),
                Is.EqualTo("something else"));

            Assert.That(File.ReadAllText(Path.Combine(_testDirectory, Child2SubDir, "notRelated.txt")),
                Is.EqualTo("something else ttt"));

            Assert.That(File.ReadAllText(Path.Combine(_testDirectory, SharedCoreDir, "file.dll")),
                Is.EqualTo("newModifiedFile_content"));

            Assert.That(File.ReadAllText(Path.Combine(_testDirectory, SharedCoreDir, "file.pdb")),
                Is.EqualTo("newModifiedFile_pdb_content"));

            Assert.That(File.ReadAllText(Path.Combine(_testDirectory, SharedCoreDir, "notUsedFile.dll")),
                Is.EqualTo("notUsedFile_content"));

            Assert.That(File.ReadAllText(Path.Combine(_testDirectory, SharedCoreDir, "notUsedFile.pdb")),
                Is.EqualTo("notUsedFile_pdb_content"));

            Assert.That(File.ReadAllText(Path.Combine(_testDirectory, "file.dll")),
                Is.EqualTo("outside modification tree"));
        }
    }
}
