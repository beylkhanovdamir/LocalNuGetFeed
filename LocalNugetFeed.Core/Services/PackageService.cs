using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using LocalNugetFeed.Core.Entities;
using LocalNugetFeed.Core.Extensions;
using LocalNugetFeed.Core.Interfaces;
using LocalNugetFeed.Core.Models;
using Microsoft.AspNetCore.Http;
using NuGet.Packaging;
using NuGet.Versioning;

namespace LocalNugetFeed.Core.Services
{
	public class PackageService : IPackageService
	{
		private readonly IPackageFileStorageService _storageService;
		private readonly IPackageSessionService _sessionService;
		private IReadOnlyList<Package> Packages => _sessionService.Get();

		public PackageService(IPackageFileStorageService storageService, IPackageSessionService sessionService)
		{
			_storageService = storageService;
			_sessionService = sessionService;
		}

		/// <summary>
		/// Push package to the local feed
		/// </summary>
		/// <param name="packageFile">nuget package</param>
		/// <returns>result of request</returns>
		public async Task<ResponseModel> Push(IFormFile packageFile)
		{
			if (packageFile == null)
			{
				return new ResponseModel(HttpStatusCode.BadRequest, "Package file not found");
			}

			using (var sourceFileStream = packageFile.OpenReadStream())
			{
				using (var reader = new PackageArchiveReader(sourceFileStream))
				{
					var packageNuspec = reader.NuspecReader;

					// step 1. we should make sure that package doesn't exists in local feed
					var package = Get(packageNuspec.PackageId(), packageNuspec.PackageVersion());
					if (package != null)
					{
						return new ResponseModel(HttpStatusCode.BadRequest, "Package already exists");
					}

					// step 2. Save package locally to the feed			
					var savePackageToFileResult = await _storageService.Save(reader, sourceFileStream);

					if (!savePackageToFileResult.Success)
					{
						return new ResponseModel(savePackageToFileResult.StatusCode, savePackageToFileResult.Message);
					}

					// add new package to Session
					_sessionService.Set(savePackageToFileResult.Data);

					return new ResponseModel(HttpStatusCode.OK);
				}
			}
		}


		/// <summary>
		/// Get package by id and version
		/// </summary>
		/// <param name="id">package id</param>
		/// <param name="version">package version</param>
		/// <returns>response with result</returns>		
		public Package Get(string id, string version)
		{
			if (!Packages.Any()) return null;

			var package = Packages.FirstOrDefault(x =>
				x.Id.Equals(id, StringComparison.InvariantCultureIgnoreCase) &&
				x.Version.Equals(version, StringComparison.InvariantCultureIgnoreCase));

			return package;
		}


		/// <summary>
		/// Search packages by query in local feed (session/file system)
		/// </summary>
		/// <param name="query">search query</param>
		/// <returns>response with result</returns>		
		public async Task<ResponseModel<IReadOnlyList<Package>>> Search(string query)
		{
			// before we should check packages in session and use their if are exist there
			if (Packages == null)
			{
				// otherwise we need to load packages from file system
				var getPackagesResult = await Task.FromResult(_storageService.Read());

				if (!getPackagesResult.Success)
				{
					return new ResponseModel<IReadOnlyList<Package>>(getPackagesResult.StatusCode, getPackagesResult.Message);
				}

				var packages = getPackagesResult.Data.ToList();

				if (packages.Any())
				{
					//update packages in session storage
					_sessionService.Set(packages);
				}
				else
				{
					return new ResponseModel<IReadOnlyList<Package>>(packages, HttpStatusCode.OK);
				}
			}

			var searchResult = new List<Package>(Packages);
			if (!string.IsNullOrEmpty(query))
			{
				query = query.ToLowerInvariant();
				searchResult = searchResult.Where(x => x.Id.ToLowerInvariant().Contains(query) || x.Description.ToLowerInvariant().Contains(query)).ToList();
			}

			searchResult = searchResult.OrderByDescending(s => new NuGetVersion(s.Version))
				.GroupBy(g => g.Id)
				.Select(z => z.First()).ToList();

			return new ResponseModel<IReadOnlyList<Package>>(searchResult, HttpStatusCode.OK);
		}
	}
}