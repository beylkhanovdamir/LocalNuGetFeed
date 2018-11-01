using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using LocalNugetFeed.Core.Common;
using LocalNugetFeed.Core.Entities;
using LocalNugetFeed.Core.Interfaces;
using LocalNugetFeed.Core.Models;
using LocalNugetFeed.Core.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Internal;
using Moq;
using NuGet.Packaging;
using NuGet.Versioning;
using Xunit;

namespace LocalNuGetFeed.Core.Tests
{
	public class PackageServiceTest
	{
		private readonly Mock<IPackageFileStorageService> _mockPackageFileStorageService;
		private readonly Mock<IPackageSessionService> _mockPackageSessionService;

		private readonly PackageService _packageService;
		private string _mockFilePath => TestPackageHelper.GetOSVersionPackageFilePath();

		public PackageServiceTest()
		{
			_mockPackageFileStorageService = new Mock<IPackageFileStorageService>();
			_mockPackageSessionService = new Mock<IPackageSessionService>();
			_packageService = new PackageService(_mockPackageFileStorageService.Object, _mockPackageSessionService.Object);
		}

		[Fact]
		public async Task Push_ReturnsSuccessfulResponse()
		{
			using (var stream = new MemoryStream(File.ReadAllBytes(_mockFilePath)))
			{
				var _mockFile = new FormFile(stream, 0, stream.Length, TestPackageHelper.TestPackageId,
					$"{TestPackageHelper.TestPackageId}.{TestPackageHelper.TestPackageVersion}.nupkg");
				stream.Seek(0, SeekOrigin.Begin);

				// setup
				_mockPackageFileStorageService.Setup(s => s.Save(It.IsAny<PackageArchiveReader>(), It.IsAny<Stream>()))
					.ReturnsAsync(new ResponseModel<Package>(HttpStatusCode.OK));
				_mockPackageSessionService.Setup(s => s.Set(It.IsAny<Package>()));
				_mockPackageSessionService.Setup(s => s.Get()).Returns(() => new[] {TestPackageHelper.GetMockPackage()});

				// Act
				var result = await _packageService.Push(_mockFile);

				// Assert
				Assert.True(result.Success);
			}
		}

		[Fact]
		public async Task Push_ReturnsBadRequestResponse_WhenPackageIsAlreadyExists()
		{
			using (var stream = new MemoryStream(File.ReadAllBytes(_mockFilePath)))
			{
				var _mockFile = new FormFile(stream, 0, stream.Length, TestPackageHelper.TestPackageId,
					$"{TestPackageHelper.TestPackageId}.{TestPackageHelper.TestPackageVersion}.nupkg");
				stream.Seek(0, SeekOrigin.Begin);

				// setup
				_mockPackageFileStorageService.Setup(s => s.Save(It.IsAny<PackageArchiveReader>(), It.IsAny<Stream>()))
					.ReturnsAsync(new ResponseModel<Package>(HttpStatusCode.OK));
				_mockPackageSessionService.Setup(s => s.Set(It.IsAny<Package>()));
				_mockPackageSessionService.Setup(s => s.Get()).Returns(() => new[] {TestPackageHelper.GetOSVersionPackage()});

				// Act
				var packageService = new PackageService(_mockPackageFileStorageService.Object, _mockPackageSessionService.Object);
				var result = await packageService.Push(_mockFile);

				// Assert
				Assert.False(result.Success);
				Assert.True(result.StatusCode == HttpStatusCode.Conflict);
			}
		}

		[Fact]
		public async Task Push_ReturnsFailedResponse_PackageFileIsNull()
		{
			var packageService = new PackageService(_mockPackageFileStorageService.Object, _mockPackageSessionService.Object);
			var result = await packageService.Push(null);

			// Assert
			Assert.False(result.Success);
			Assert.True(result.StatusCode == HttpStatusCode.BadRequest);
		}

		[Fact]
		public async Task Push_ReturnsFailedResponse_PackageFileIsIncorrect()
		{
			var result = await _packageService.Push(TestPackageHelper.GetMockFile("some content", "wrongPackageFileExtension.txt"));
			// Assert
			Assert.False(result.Success);
			Assert.True(result.StatusCode == HttpStatusCode.UnsupportedMediaType);
			Assert.IsType<InvalidDataException>(result.ExceptionDetails);
		}

		[Fact]
		public async Task Search_ReturnsPackagesFilteredByVersionDesc_WhenQueryIsEmpty()
		{
			// setup
			_mockPackageSessionService.Setup(s => s.Get()).Returns(() => TwoTestPackageVersions);

			// Act
			var result = await _packageService.Search();

			// Assert
			Assert.True(result.Success);
			Assert.True(result.Data.Any());
			Assert.NotNull(result.Data.Single());
		}

		[Theory]
		[InlineData("", true)]
		[InlineData("TestPackage", true)]
		[InlineData("UnknownPackage", false)]
		public async Task Search_ReturnsContentOrNot_WhenQueryIsExists(string query, bool isExist)
		{
			// setup
			var searchResult = TwoTestPackageVersions.Where(x => string.IsNullOrWhiteSpace(query) || x.Id.Contains(query, StringComparison.OrdinalIgnoreCase))
				.OrderByDescending(s => new NuGetVersion(s.Version))
				.GroupBy(g => g.Id)
				.Select(z => z.First()).ToList();
			_mockPackageSessionService.Setup(s => s.Get()).Returns(() => TwoTestPackageVersions);

			// Act
			var result = await _packageService.Search(query);

			// Assert
			if (isExist)
			{
				Assert.True(result.Success);
				Assert.NotNull(result.Data);
				Assert.True(result.Data.Any());
				Assert.True(result.Data.Count == 1); // we should get only the latest version of TestPackage package
				Assert.True(result.Data.First().Version == searchResult.First().Version);
			}
			else
			{
				Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
			}
		}

		[Fact]
		public async Task Search_ReturnsNotFoundResult()
		{
			// setup
			_mockPackageSessionService.Setup(s => s.Get()).Returns(() => new List<Package>());
			_mockPackageFileStorageService.Setup(s => s.Read()).Returns(() => new ResponseModel<IReadOnlyList<Package>>(HttpStatusCode.NotFound));

			// Act
			var result = await _packageService.Search();

			// Assert
			Assert.False(result.Success);
			Assert.True(result.StatusCode == HttpStatusCode.NotFound);
		}

		[Theory]
		[InlineData(MyTestPackageId, true)]
		[InlineData("UnknownPackage", false)]
		public async Task PackageVersions_ReturnsPackageVersionsOrNotFound(string packageId, bool isExist)
		{
			// setup
			_mockPackageSessionService.Setup(s => s.Get()).Returns(() => TwoTestPackageVersions);

			// Act
			var result = await _packageService.PackageVersions(packageId);

			// Assert
			if (isExist)
			{
				Assert.True(result.Success);
				Assert.NotNull(result.Data);
				Assert.True(result.Data.Any());
				Assert.True(result.Data.Count == 2); // we should get only the latest version of TestPackage package
			}
			else
			{
				Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
			}
		}

		[Fact]
		public async Task PackageVersions_ReturnsNotFoundResult()
		{
			// setup
			_mockPackageSessionService.Setup(s => s.Get()).Returns(() => new List<Package>());
			_mockPackageFileStorageService.Setup(s => s.Read()).Returns(() => new ResponseModel<IReadOnlyList<Package>>(HttpStatusCode.NotFound));

			// Act
			var result = await _packageService.PackageVersions(MyTestPackageId);

			// Assert
			Assert.False(result.Success);
			Assert.True(result.StatusCode == HttpStatusCode.NotFound);
		}
		
		[Theory]
		[InlineData("mytestpackage", "1.0.0", true)]
		[InlineData("mytest", "1.0.0", false)]
		[InlineData(MyTestPackageId, "1.0.0", true)]
		[InlineData(MyTestPackageId, "2.0.0", false)]
		[InlineData("UnknownPackage","1.0.0", false)]
		public async Task GetPackage_ReturnsPackageOrNotFound(string packageId, string packageVersion, bool isExist)
		{
			// setup
			_mockPackageSessionService.Setup(s => s.Get()).Returns(() => TwoTestPackageVersions);

			// Act
			var result = await _packageService.GetPackage(packageId, packageVersion);

			// Assert
			if (isExist)
			{
				Assert.True(result.Success);
				Assert.NotNull(result.Data);
			}
			else
			{
				Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
			}
		}
		
		[Fact]
		public async Task GetPackage_ReturnsNotFoundResult()
		{
			// setup
			_mockPackageSessionService.Setup(s => s.Get()).Returns(() => new List<Package>());
			_mockPackageFileStorageService.Setup(s => s.Read()).Returns(() => new ResponseModel<IReadOnlyList<Package>>(HttpStatusCode.NotFound));

			// Act
			var result = await _packageService.GetPackage(MyTestPackageId, "1.0.0");

			// Assert
			Assert.False(result.Success);
			Assert.True(result.StatusCode == HttpStatusCode.NotFound);
		}
		
		[Fact]
		public async Task GetPackages_ReturnsDataFromSession()
		{
			// setup
			_mockPackageSessionService.Setup(s => s.Get()).Returns(() => TwoTestPackageVersions);

			// Act
			var result = await _packageService.GetPackages();

			// Assert
			Assert.True(result.Success);
			Assert.True(result.Data.Any());
			Assert.True(result.Data.Count == 2);
		}
		
		[Fact]
		public async Task GetPackages_ReturnsDataFromFileSystem()
		{
			// setup
			_mockPackageSessionService.Setup(s => s.Get()).Returns(() => null);
			_mockPackageFileStorageService.Setup(s => s.Read()).Returns(() => new ResponseModel<IReadOnlyList<Package>>(HttpStatusCode.OK, TwoTestPackageVersions));

			// Act
			var result = await _packageService.GetPackages();

			// Assert
			Assert.True(result.Success);
			Assert.True(result.Data.Any());
			Assert.True(result.Data.Count == 2);
		}
		
		[Fact]
		public async Task GetPackages_ReturnsBadRequestWhenNoAnyPackages()
		{
			// setup
			_mockPackageSessionService.Setup(s => s.Get()).Returns(() => null);
			_mockPackageFileStorageService.Setup(s => s.Read()).Returns(() => new ResponseModel<IReadOnlyList<Package>>(HttpStatusCode.NotFound));

			// Act
			var result = await _packageService.GetPackages();

			// Assert
			Assert.False(result.Success);
			Assert.Null(result.Data);
			Assert.True(result.StatusCode == HttpStatusCode.NotFound);
		}



		private static IReadOnlyList<Package> TwoTestPackageVersions => new List<Package>()
		{
			new Package()
			{
				Id = MyTestPackageId,
				Description = "Package description",
				Authors = "D.B.",
				Version = "1.0.0"
			},
			new Package()
			{
				Id = MyTestPackageId,
				Description = "Package description",
				Authors = "D.B.",
				Version = "1.0.1"
			}
		};

		private const string MyTestPackageId = "MyTestPackage";

	


	}
}