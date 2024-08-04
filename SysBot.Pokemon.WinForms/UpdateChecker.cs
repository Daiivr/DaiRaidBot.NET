﻿using Newtonsoft.Json;
using SysBot.Pokemon.SV.BotRaid.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace SysBot.Pokemon.WinForms
{
    public class UpdateChecker
    {
        private const string RepositoryOwner = "Daiivr";
        private const string RepositoryName = "DaiRaidBot.NET";

        public static async Task<(bool UpdateAvailable, bool UpdateRequired, string NewVersion)> CheckForUpdatesAsync()
        {
            ReleaseInfo latestRelease = await FetchLatestReleaseAsync();

            bool updateAvailable = latestRelease != null && latestRelease.TagName != DaiRaidBot.Version;
            bool updateRequired = latestRelease?.Prerelease == false && IsUpdateRequired(latestRelease.Body);
            string? newVersion = latestRelease?.TagName;

            if (updateAvailable)
            {
                UpdateForm updateForm = new(updateRequired, newVersion);
                updateForm.ShowDialog();
            }

            return (updateAvailable, updateRequired, newVersion);
        }

        public static async Task<string> FetchChangelogAsync()
        {
            ReleaseInfo latestRelease = await FetchLatestReleaseAsync();

            if (latestRelease == null)
                return "No se pudo obtener la información de la última versión.";

            return latestRelease.Body;
        }

        public static async Task<string?> FetchDownloadUrlAsync()
        {
            ReleaseInfo latestRelease = await FetchLatestReleaseAsync();

            if (latestRelease == null)
                return null;

            string? downloadUrl = latestRelease.Assets.FirstOrDefault(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))?.BrowserDownloadUrl;

            return downloadUrl;
        }

        private static async Task<ReleaseInfo?> FetchLatestReleaseAsync()
        {
            using var client = new HttpClient();
            try
            {
                // Add a custom header to identify the request
                client.DefaultRequestHeaders.Add("User-Agent", "DaiRaidBot.NET");

                string releasesUrl = $"http://api.github.com/repos/{RepositoryOwner}/{RepositoryName}/releases/latest";
                HttpResponseMessage response = await client.GetAsync(releasesUrl);

                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                string jsonContent = await response.Content.ReadAsStringAsync();
                ReleaseInfo release = JsonConvert.DeserializeObject<ReleaseInfo>(jsonContent);

                return release;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static bool IsUpdateRequired(string changelogBody)
        {
            return !string.IsNullOrWhiteSpace(changelogBody) &&
                   changelogBody.Contains("Required = Yes", StringComparison.OrdinalIgnoreCase);
        }

        private class ReleaseInfo
        {
            [JsonProperty("tag_name")]
            public string? TagName { get; set; }

            [JsonProperty("prerelease")]
            public bool Prerelease { get; set; }

            [JsonProperty("assets")]
            public List<AssetInfo>? Assets { get; set; }

            [JsonProperty("body")]
            public string? Body { get; set; }
        }

        private class AssetInfo
        {
            [JsonProperty("name")]
            public string? Name { get; set; }

            [JsonProperty("browser_download_url")]
            public string? BrowserDownloadUrl { get; set; }
        }
    }
}