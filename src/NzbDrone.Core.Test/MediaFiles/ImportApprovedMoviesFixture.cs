using System.Collections.Generic;
using System.IO;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.MediaFiles.MovieImport;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles
{
    [TestFixture]

    //TODO: Update all of this for movies.
    public class ImportApprovedMoviesFixture : CoreTest<ImportApprovedMovie>
    {
        private List<ImportDecision> _rejectedDecisions;
        private List<ImportDecision> _approvedDecisions;

        private DownloadClientItem _downloadClientItem;
        private string _simpleFileName;
        private string _collectionName;

        [SetUp]
        public void Setup()
        {
            _rejectedDecisions = new List<ImportDecision>();
            _approvedDecisions = new List<ImportDecision>();

            var outputPath = @"C:\Test\Unsorted\movies\".AsOsAgnostic();
            _simpleFileName = "movie-720p.mkv";
            _collectionName = "Transformers.Collection.720p.BluRay.x264-Radarr";

            var movie = Builder<Movie>.CreateNew()
                .With(e => e.Profile = new Profile { Items = Qualities.QualityFixture.GetDefaultQualities() })
                .With(s => s.Path = @"C:\Test\movies\Transformers (2007)".AsOsAgnostic())
                .With(s => s.Id = 1)
                .Build();

            var movie2 = Builder<Movie>.CreateNew()
                .With(e => e.Profile = new Profile { Items = Qualities.QualityFixture.GetDefaultQualities() })
                .With(s => s.Path = @"C:\Test\movies\Transformers 2 (2009)".AsOsAgnostic())
                .With(s => s.Id = 2)
                .Build();

            _rejectedDecisions.Add(new ImportDecision(new LocalMovie(), new Rejection("Rejected!")));
            _rejectedDecisions.Add(new ImportDecision(new LocalMovie(), new Rejection("Rejected!")));
            _rejectedDecisions.Add(new ImportDecision(new LocalMovie(), new Rejection("Rejected!")));

            _approvedDecisions.Add(new ImportDecision(
                                       new LocalMovie
                                       {
                                           Movie = movie,
                                           Path = Path.Combine(movie.Path, "Transformers 2007 720p.mkv"),
                                           Quality = new QualityModel(),
                                           ReleaseGroup = "Radarr"
                                       }));

            _approvedDecisions.Add(new ImportDecision(
                                       new LocalMovie
                                       {
                                           Movie = movie2,
                                           Path = Path.Combine(movie2.Path, "Transformers 2 2009 720p.mkv"),
                                           Quality = new QualityModel(),
                                           ReleaseGroup = "Radarr"
                                       }));

            Mocker.GetMock<IUpgradeMediaFiles>()
                  .Setup(s => s.UpgradeMovieFile(It.IsAny<MovieFile>(), It.IsAny<LocalMovie>(), It.IsAny<bool>()))
                  .Returns(new MovieFileMoveResult());

            Mocker.GetMock<IHistoryService>()
                .Setup(x => x.FindByDownloadId(It.IsAny<string>()))
                .Returns(new List<MovieHistory>());

            _downloadClientItem = Builder<DownloadClientItem>.CreateNew()
                                                             .With(d => d.OutputPath = new OsPath(outputPath))
                                                             .Build();
        }

        private void GivenNewDownload()
        {
            _approvedDecisions.ForEach(a => a.LocalMovie.Path = Path.Combine(_downloadClientItem.OutputPath.ToString(), Path.GetFileName(a.LocalMovie.Path)));
        }

        private void GivenNewDownloadInFolder()
        {
            _approvedDecisions.ForEach(a => a.LocalMovie.Path = Path.Combine(_downloadClientItem.OutputPath.ToString(), Path.GetFileNameWithoutExtension(a.LocalMovie.Path), _simpleFileName));
        }

        private void GivenNewCollectionDownload(DownloadClientItem downloadClientItem = null)
        {
            var name = "Transformers.2007.720p.BluRay.x264-Radarr";
            var name2 = "Transformers.2.2009.720p.BluRay.x264-Radarr";
            var outputPath = Path.Combine(@"C:\Test\Unsorted\movies\".AsOsAgnostic(), _collectionName);
            if (downloadClientItem != null)
            {
                downloadClientItem.OutputPath = new OsPath(outputPath);
            }

            var localMovie1 = _approvedDecisions.First().LocalMovie;
            localMovie1.FileMovieInfo = new ParsedMovieInfo { OriginalTitle = name };
            localMovie1.Path = Path.Combine(outputPath, name + ".mkv");

            var localMovie2 = _approvedDecisions.Last().LocalMovie;
            localMovie2.FileMovieInfo = new ParsedMovieInfo { OriginalTitle = name2 };
            localMovie2.Path = Path.Combine(outputPath, name2 + ".mkv");
        }

        private void GivenNewCollectionDownloadInFolders(DownloadClientItem downloadClientItem = null)
        {
            var name = "Transformers.2007.720p.BluRay.x264-Radarr";
            var name2 = "Transformers.2.2009.720p.BluRay.x264-Radarr";
            var outputPath = Path.Combine(@"C:\Test\Unsorted\movies\".AsOsAgnostic(), _collectionName);
            if (downloadClientItem != null)
            {
                downloadClientItem.OutputPath = new OsPath(outputPath);
            }

            var localMovie1 = _approvedDecisions.First().LocalMovie;
            localMovie1.FileMovieInfo = new ParsedMovieInfo { OriginalTitle = _simpleFileName };
            localMovie1.FolderMovieInfo = new ParsedMovieInfo { OriginalTitle = name };
            localMovie1.Path = Path.Combine(outputPath, name, _simpleFileName);

            var localMovie2 = _approvedDecisions.Last().LocalMovie;
            localMovie2.FileMovieInfo = new ParsedMovieInfo { OriginalTitle = _simpleFileName };
            localMovie2.FolderMovieInfo = new ParsedMovieInfo { OriginalTitle = name2 };
            localMovie2.Path = Path.Combine(outputPath, name2, _simpleFileName);
        }

        [Test]
        public void should_not_import_any_if_there_are_no_approved_decisions()
        {
            Subject.Import(_rejectedDecisions, false).Where(i => i.Result == ImportResultType.Imported).Should().BeEmpty();

            Mocker.GetMock<IMediaFileService>().Verify(v => v.Add(It.IsAny<MovieFile>()), Times.Never());
        }

        [Test]
        public void should_import_each_approved()
        {
            Subject.Import(_approvedDecisions, false).Should().HaveCount(2);
        }

        [Test]
        public void should_only_import_approved()
        {
            var all = new List<ImportDecision>();
            all.AddRange(_rejectedDecisions);
            all.AddRange(_approvedDecisions);

            var result = Subject.Import(all, false);

            result.Should().HaveCount(all.Count);
            result.Where(i => i.Result == ImportResultType.Imported).Should().HaveCount(_approvedDecisions.Count);
        }

        [Test]
        public void should_only_import_each_movie_once()
        {
            var all = new List<ImportDecision>();
            all.AddRange(_approvedDecisions);
            all.Add(new ImportDecision(_approvedDecisions.First().LocalMovie));

            var result = Subject.Import(all, false);

            result.Where(i => i.Result == ImportResultType.Imported).Should().HaveCount(_approvedDecisions.Count);
        }

        [Test]
        public void should_move_new_downloads()
        {
            Subject.Import(new List<ImportDecision> { _approvedDecisions.First() }, true);

            Mocker.GetMock<IUpgradeMediaFiles>()
                  .Verify(v => v.UpgradeMovieFile(It.IsAny<MovieFile>(), _approvedDecisions.First().LocalMovie, false),
                          Times.Once());
        }

        [Test]
        public void should_publish_MovieImportedEvent_for_new_downloads()
        {
            Subject.Import(new List<ImportDecision> { _approvedDecisions.First() }, true);

            Mocker.GetMock<IEventAggregator>()
                .Verify(v => v.PublishEvent(It.IsAny<MovieImportedEvent>()), Times.Once());
        }

        [Test]
        public void should_not_move_existing_files()
        {
            Subject.Import(new List<ImportDecision> { _approvedDecisions.First() }, false);

            Mocker.GetMock<IUpgradeMediaFiles>()
                  .Verify(v => v.UpgradeMovieFile(It.IsAny<MovieFile>(), _approvedDecisions.First().LocalMovie, false),
                          Times.Never());
        }

        [Test]
        public void should_use_nzb_title_as_scene_name()
        {
            GivenNewDownload();
            _downloadClientItem.Title = "Transformers.2007.720p.BluRay.x264-Radarr";

            Subject.Import(new List<ImportDecision> { _approvedDecisions.First() }, true, _downloadClientItem);

            Mocker.GetMock<IMediaFileService>().Verify(v => v.Add(It.Is<MovieFile>(c => c.SceneName == _downloadClientItem.Title)));
        }

        [TestCase(".mkv")]
        [TestCase(".par2")]
        [TestCase(".nzb")]
        public void should_remove_extension_from_nzb_title_for_scene_name(string extension)
        {
            GivenNewDownload();
            var title = "Transformers.2007.720p.BluRay.x264-Radarr";

            _downloadClientItem.Title = title + extension;

            Subject.Import(new List<ImportDecision> { _approvedDecisions.First() }, true, _downloadClientItem);

            Mocker.GetMock<IMediaFileService>().Verify(v => v.Add(It.Is<MovieFile>(c => c.SceneName == title)));
        }

        [Test]
        public void should_not_set_any_scene_name()
        {
            GivenNewDownload();
            _approvedDecisions.First().LocalMovie.Path = Path.Combine(_downloadClientItem.OutputPath.ToString(), "transformers1", "rdr-transformers-720p.mkv");
            _downloadClientItem.Title = "Transformers 2007";

            Subject.Import(new List<ImportDecision> { _approvedDecisions.First() }, true, _downloadClientItem);

            Mocker.GetMock<IMediaFileService>().Verify(v => v.Add(It.Is<MovieFile>(c => c.SceneName == null)));
        }

        [Test]
        public void should_use_file_name_as_scenename_only_if_it_looks_like_scenename()
        {
            GivenNewDownload();
            _approvedDecisions.First().LocalMovie.Path = Path.Combine(_downloadClientItem.OutputPath.ToString(), "Transformers.2007.720p.BluRay.x264-Radarr.mkv");
            _approvedDecisions.First().LocalMovie.FileMovieInfo = new ParsedMovieInfo { OriginalTitle = "Transformers.2007.720p.BluRay.x264-Radarr.mkv" };

            Subject.Import(new List<ImportDecision> { _approvedDecisions.First() }, true);

            Mocker.GetMock<IMediaFileService>().Verify(v => v.Add(It.Is<MovieFile>(c => c.SceneName == "Transformers.2007.720p.BluRay.x264-Radarr")));
        }

        [Test]
        public void should_not_use_file_name_as_scenename_if_it_doesnt_looks_like_scenename()
        {
            GivenNewDownload();
            _approvedDecisions.First().LocalMovie.Path = Path.Combine(_downloadClientItem.OutputPath.ToString(), "rdr-transformers-720p.mkv");
            _approvedDecisions.First().LocalMovie.FileMovieInfo = new ParsedMovieInfo { OriginalTitle = "rdr-transformers-720p.mkv" };

            Subject.Import(new List<ImportDecision> { _approvedDecisions.First() }, true);

            Mocker.GetMock<IMediaFileService>().Verify(v => v.Add(It.Is<MovieFile>(c => c.SceneName == null)));
        }

        [Test]
        public void should_use_folder_name_as_scenename_only_if_it_looks_like_scenename()
        {
            GivenNewDownload();
            _approvedDecisions.First().LocalMovie.Path = Path.Combine(_downloadClientItem.OutputPath.ToString(), "Transformers.2007.720p.BluRay.x264-Radarr", "rdr-transformers-720p.mkv");
            _approvedDecisions.First().LocalMovie.FileMovieInfo = new ParsedMovieInfo { OriginalTitle = "rdr-transformers-720p.mkv" };
            _approvedDecisions.First().LocalMovie.FolderMovieInfo = new ParsedMovieInfo { OriginalTitle = "Transformers.2007.720p.BluRay.x264-Radarr" };

            Subject.Import(new List<ImportDecision> { _approvedDecisions.First() }, true);

            Mocker.GetMock<IMediaFileService>().Verify(v => v.Add(It.Is<MovieFile>(c => c.SceneName == "Transformers.2007.720p.BluRay.x264-Radarr")));
        }

        [Test]
        public void should_not_use_folder_name_as_scenename_if_it_doesnt_looks_like_scenename()
        {
            GivenNewDownload();
            _approvedDecisions.First().LocalMovie.Path = Path.Combine(_downloadClientItem.OutputPath.ToString(), "transformers", "rdr-transformers-720p.mkv");
            _approvedDecisions.First().LocalMovie.FileMovieInfo = new ParsedMovieInfo { OriginalTitle = "rdr-transformers-720p.mkv" };
            _approvedDecisions.First().LocalMovie.FolderMovieInfo = new ParsedMovieInfo { OriginalTitle = "transformers" };

            Subject.Import(new List<ImportDecision> { _approvedDecisions.First() }, true);

            Mocker.GetMock<IMediaFileService>().Verify(v => v.Add(It.Is<MovieFile>(c => c.SceneName == null)));
        }

        //TODO Write test should use foldername if it looks like scenename
        [Test]
        public void should_import_larger_files_first()
        {
            var fileDecision = _approvedDecisions.First();
            fileDecision.LocalMovie.Size = 1.Gigabytes();

            var sampleDecision = new ImportDecision(
                new LocalMovie
                {
                    Movie = fileDecision.LocalMovie.Movie,
                    Path = @"C:\Test\movies\Transformers (2007)\30 Rock - 2017 - Pilot.avi".AsOsAgnostic(),
                    Quality = new QualityModel(),
                    Size = 80.Megabytes()
                });

            var all = new List<ImportDecision>();
            all.Add(fileDecision);
            all.Add(sampleDecision);

            var results = Subject.Import(all, false);

            results.Should().HaveCount(all.Count);
            results.Should().ContainSingle(d => d.Result == ImportResultType.Imported);
            results.Should().ContainSingle(d => d.Result == ImportResultType.Imported && d.ImportDecision.LocalMovie.Size == fileDecision.LocalMovie.Size);
        }

        [Test]
        public void should_copy_when_cannot_move_files_downloads()
        {
            GivenNewDownload();
            _downloadClientItem.Title = "30.Rock.S01E01";
            _downloadClientItem.CanMoveFiles = false;

            Subject.Import(new List<ImportDecision> { _approvedDecisions.First() }, true, _downloadClientItem);

            Mocker.GetMock<IUpgradeMediaFiles>()
                  .Verify(v => v.UpgradeMovieFile(It.IsAny<MovieFile>(), _approvedDecisions.First().LocalMovie, true), Times.Once());
        }

        [Test]
        public void should_use_override_importmode()
        {
            GivenNewDownload();
            _downloadClientItem.Title = "30.Rock.S01E01";
            _downloadClientItem.CanMoveFiles = false;

            Subject.Import(new List<ImportDecision> { _approvedDecisions.First() }, true, _downloadClientItem, ImportMode.Move);

            Mocker.GetMock<IUpgradeMediaFiles>()
                  .Verify(v => v.UpgradeMovieFile(It.IsAny<MovieFile>(), _approvedDecisions.First().LocalMovie, false), Times.Once());
        }

        [Test]
        public void should_use_file_name_only_for_download_client_item_without_a_job_folder()
        {
            var fileName = "Transformers.2007.720p.BluRay.x264-Radarr.mkv";
            var path = Path.Combine(@"C:\Test\Unsorted\movies\".AsOsAgnostic(), fileName);

            _downloadClientItem.OutputPath = new OsPath(path);
            _approvedDecisions.First().LocalMovie.Path = path;

            Subject.Import(new List<ImportDecision> { _approvedDecisions.First() }, true, _downloadClientItem);

            Mocker.GetMock<IMediaFileService>().Verify(v => v.Add(It.Is<MovieFile>(c => c.OriginalFilePath == fileName)));
        }

        [Test]
        public void should_use_folder_and_file_name_only_for_download_client_item_with_a_job_folder()
        {
            var name = "Transformers.2007.720p.BluRay.x264-Radarr";
            var outputPath = Path.Combine(@"C:\Test\Unsorted\movies\".AsOsAgnostic(), name);

            _downloadClientItem.OutputPath = new OsPath(outputPath);
            _approvedDecisions.First().LocalMovie.Path = Path.Combine(outputPath, name + ".mkv");

            Subject.Import(new List<ImportDecision> { _approvedDecisions.First() }, true, _downloadClientItem);

            Mocker.GetMock<IMediaFileService>().Verify(v => v.Add(It.Is<MovieFile>(c => c.OriginalFilePath == $"{name}\\{name}.mkv".AsOsAgnostic())));
        }

        [Test]
        public void should_include_intermediate_folders_for_download_client_item_with_a_job_folder()
        {
            var name = "Transformers.2007.720p.BluRay.x264-Radarr";
            var outputPath = Path.Combine(@"C:\Test\Unsorted\movies\".AsOsAgnostic(), name);

            _downloadClientItem.OutputPath = new OsPath(outputPath);
            _approvedDecisions.First().LocalMovie.Path = Path.Combine(outputPath, "subfolder", name + ".mkv");

            Subject.Import(new List<ImportDecision> { _approvedDecisions.First() }, true, _downloadClientItem);

            Mocker.GetMock<IMediaFileService>().Verify(v => v.Add(It.Is<MovieFile>(c => c.OriginalFilePath == $"{name}\\subfolder\\{name}.mkv".AsOsAgnostic())));
        }

        //TODO Test scenename parsing (for standard release, reversed release, and originalfilepath )
        [Test]
        public void should_use_folder_info_original_title_to_find_relative_path()
        {
            var name = "Transformers.2007.720p.BluRay.x264-Radarr";
            var outputPath = Path.Combine(@"C:\Test\Unsorted\movies\".AsOsAgnostic(), name);
            var localEpisode = _approvedDecisions.First().LocalMovie;

            localEpisode.FolderMovieInfo = new ParsedMovieInfo { OriginalTitle = name };
            localEpisode.Path = Path.Combine(outputPath, "subfolder", name + ".mkv");

            Subject.Import(new List<ImportDecision> { _approvedDecisions.First() }, true, null);

            Mocker.GetMock<IMediaFileService>().Verify(v => v.Add(It.Is<MovieFile>(c => c.OriginalFilePath == $"{name}\\subfolder\\{name}.mkv".AsOsAgnostic())));
        }

        [Test]
        public void should_use_scene_name_from_folder_name()
        {
            GivenNewCollectionDownloadInFolders();

            var results = Subject.Import(_approvedDecisions, true, null);
            results.Should().HaveCount(_approvedDecisions.Count);
            results.Should().OnlyContain(d => d.Result == ImportResultType.Imported);

            Mocker.GetMock<IMediaFileService>().Verify(v => v.Add(It.Is<MovieFile>(c => c.OriginalFilePath == $"{_approvedDecisions.First().LocalMovie.FolderMovieInfo.OriginalTitle}\\{_simpleFileName}".AsOsAgnostic() && c.SceneName == _approvedDecisions.First().LocalMovie.FolderMovieInfo.OriginalTitle)));
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Add(It.Is<MovieFile>(c => c.OriginalFilePath == $"{_approvedDecisions.Last().LocalMovie.FolderMovieInfo.OriginalTitle}\\{_simpleFileName}".AsOsAgnostic() && c.SceneName == _approvedDecisions.Last().LocalMovie.FolderMovieInfo.OriginalTitle)));
        }

        [Test]
        public void should_use_scene_name_from_file_name()
        {
            GivenNewCollectionDownload();

            var results = Subject.Import(_approvedDecisions, true, null);
            results.Should().HaveCount(_approvedDecisions.Count);
            results.Should().OnlyContain(d => d.Result == ImportResultType.Imported);

            Mocker.GetMock<IMediaFileService>().Verify(v => v.Add(It.Is<MovieFile>(c => c.OriginalFilePath == $"{_collectionName}\\{_approvedDecisions.First().LocalMovie.FileMovieInfo.OriginalTitle}.mkv".AsOsAgnostic() && c.SceneName == _approvedDecisions.First().LocalMovie.FileMovieInfo.OriginalTitle)));
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Add(It.Is<MovieFile>(c => c.OriginalFilePath == $"{_collectionName}\\{_approvedDecisions.Last().LocalMovie.FileMovieInfo.OriginalTitle}.mkv".AsOsAgnostic() && c.SceneName == _approvedDecisions.Last().LocalMovie.FileMovieInfo.OriginalTitle)));
        }

        [Test]
        public void should_use_folder_name_as_scene_name_first_if_no_download_client_item()
        {
            GivenNewCollectionDownloadInFolders();

            var results = Subject.Import(_approvedDecisions, true, null);
            results.Should().HaveCount(_approvedDecisions.Count);
            results.Should().OnlyContain(d => d.Result == ImportResultType.Imported);

            Mocker.GetMock<IMediaFileService>().Verify(v => v.Add(It.Is<MovieFile>(c => c.OriginalFilePath == $"{_approvedDecisions.First().LocalMovie.FolderMovieInfo.OriginalTitle}\\{_simpleFileName}".AsOsAgnostic() && c.SceneName == _approvedDecisions.First().LocalMovie.FolderMovieInfo.OriginalTitle)));
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Add(It.Is<MovieFile>(c => c.OriginalFilePath == $"{_approvedDecisions.Last().LocalMovie.FolderMovieInfo.OriginalTitle}\\{_simpleFileName}".AsOsAgnostic() && c.SceneName == _approvedDecisions.Last().LocalMovie.FolderMovieInfo.OriginalTitle)));
        }

        [Test]
        public void should_use_folder_name_as_scene_name_first_if_multiple_media_files_have_same_download_client_title()
        {
            GivenNewCollectionDownloadInFolders(_downloadClientItem);
            _downloadClientItem.Title = _collectionName;

            var results = Subject.Import(_approvedDecisions, true, _downloadClientItem);
            results.Should().HaveCount(_approvedDecisions.Count);
            results.Should().OnlyContain(d => d.Result == ImportResultType.Imported);

            Mocker.GetMock<IMediaFileService>().Verify(v => v.Add(It.Is<MovieFile>(c => c.OriginalFilePath == $"{_downloadClientItem.Title}\\{_approvedDecisions.First().LocalMovie.FolderMovieInfo.OriginalTitle}\\{_simpleFileName}".AsOsAgnostic() && c.SceneName == _approvedDecisions.First().LocalMovie.FolderMovieInfo.OriginalTitle)));
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Add(It.Is<MovieFile>(c => c.OriginalFilePath == $"{_downloadClientItem.Title}\\{_approvedDecisions.Last().LocalMovie.FolderMovieInfo.OriginalTitle}\\{_simpleFileName}".AsOsAgnostic() && c.SceneName == _approvedDecisions.Last().LocalMovie.FolderMovieInfo.OriginalTitle)));
        }
    }
}
