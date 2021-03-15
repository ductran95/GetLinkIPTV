using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using RestSharp;
using RestSharp.Serializers.NewtonsoftJson;

namespace GetLinkIPTV
{
    public class Worker
    {
        private const string HostUrl = "http://vietmediaf.net/kodi1.php/";
        private const string PluginUrl = "plugin://plugin.video.vietmediaF";
        private const string MenuUrl = "plugin://plugin.video.vietmediaF?action=menu";

        private static Regex NonSpecialCharacterRegex = new Regex(@"[^\w\s]+", RegexOptions.Compiled);
        
        private readonly ILogger<Worker> _logger;
        private readonly IRestClient _restClient;
        
        public Worker(ILogger<Worker> logger)
        {
            _logger = logger;

            _restClient = new RestClient().UseNewtonsoftJson();
            _restClient.ThrowOnAnyError = true;
        }
        
        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                if (!Directory.Exists("result"))
                {
                    Directory.CreateDirectory("result");
                }
                
                var menu = await GetMenu(cancellationToken);
                await GetIPTV(menu, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Something wrong");
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Stopping ... ");
            await Task.CompletedTask;
        }

        private async Task<MenuResponse> GetMenu(CancellationToken cancellationToken)
        {
            var menuUrl = GetRealURL(MenuUrl);
            var response = await GetData<MenuResponse>(menuUrl, cancellationToken);
            _logger.LogInformation("Get menu completed, link: {link}", menuUrl);
            
            return response;
        }
        
        private async Task GetIPTV(MenuResponse menu, CancellationToken cancellationToken)
        {
            var menuItem = menu.Items.FirstOrDefault(x => x.Label == "Truyền Hình Online");
            // ReSharper disable once PossibleNullReferenceException
            var menuItemUrl = GetRealURL(menuItem.Path);
            var menuResponse = await GetData<IPTVMenuResponse>(menuItemUrl, cancellationToken);
            _logger.LogInformation("Get iptv menu completed, link: {link}", menuItemUrl);

            var tasks = new List<Task>();

            foreach (var iptvMenuItem in menuResponse.Items)
            {
                var iptvMenuItemUrl = GetRealURL(iptvMenuItem.Path);
                var iptvThreadResponse = await GetData<IPTVThreadResponse>(iptvMenuItemUrl,  cancellationToken);
                _logger.LogInformation("Get iptv thread completed, link: {link}", iptvMenuItemUrl);

                tasks.Add(GetIPTVThread(iptvThreadResponse, iptvMenuItem.Label, cancellationToken));
            }

            if (tasks.Any())
            {
                await Task.WhenAll(tasks);
            }
        }

        private async Task GetIPTVThread(IPTVThreadResponse thread, string name, CancellationToken cancellationToken)
        {
            // TODO: create m3u8 playlist
            var playableThreads = thread.Items.Where(x => x.IsPlayable && x.Path.Contains("action=play")).ToList();
            // ReSharper disable once InconsistentNaming
            var m3uBuilder = new StringBuilder();
            foreach (var iptvItem in playableThreads)
            {
                var iptvStreamUrl = GetRealURL(iptvItem.Path);
                var iptvStreamResponse = await GetData<IPTVStreamResponse>(iptvStreamUrl, cancellationToken);
                _logger.LogInformation("Get iptv stream completed, link: {link}", iptvStreamUrl);

                m3uBuilder.AppendLine(
                    $"#EXTINF:-1 tvg-name=\"{iptvItem.Label}\" tvg-logo=\"{iptvItem.Thumbnail}\",{iptvItem.Label}");
                m3uBuilder.AppendLine($"{iptvStreamResponse.Url}");
                m3uBuilder.AppendLine();
            }
            // Save playlist
            if (playableThreads.Any())
            {
                await File.WriteAllTextAsync(GetFileName(name), m3uBuilder.ToString(), cancellationToken);
            }
            
            var nonPlayableThreads = thread.Items.Where(x => !x.IsPlayable && x.Path.Contains("action=episodes")).ToList();
            var tasks = new List<Task>();
            
            foreach (var iptvItem in nonPlayableThreads)
            {
                var iptvThreadUrl = GetRealURL(iptvItem.Path);
                var iptvThreadResponse = await GetData<IPTVThreadResponse>(iptvThreadUrl, cancellationToken);
                _logger.LogInformation("Get iptv thread completed, link: {link}", iptvThreadUrl);
                
                tasks.Add(GetIPTVThread(iptvThreadResponse, iptvItem.Label, cancellationToken));
            }
            
            if (tasks.Any())
            {
                await Task.WhenAll(tasks);
            }
        }

        private async Task<T> GetData<T>(string url, CancellationToken cancellationToken)
        {
            try
            {
                var request = new RestRequest(url);
                return await _restClient.GetAsync<T>(request, cancellationToken);
            }
            catch (Exception)
            {
                _logger.LogError("Failed to get data for url: {url}, type: {type}", url, typeof(T).Name);
                throw;
            }
        }

        private string GetRealURL(string url)
        {
            return url.Replace(PluginUrl, HostUrl);
        }

        private string GetFileName(string name)
        {
            return Path.Combine("result", $"{NonSpecialCharacterRegex.Replace(name, "")}.m3u");
        }
    }

    [DataContract]
    public class MenuResponse
    {
        [DataMember(Name = "content_type")]
        public string ContentType { get; set; }
        
        [DataMember(Name = "items")]
        public List<MenuItemResponse> Items { get; set; }
    }
    
    [DataContract]
    public class MenuItemResponse
    {
        [DataMember(Name = "label")]
        public string Label { get; set; }
        
        [DataMember(Name = "path")]
        public string Path { get; set; }
        
        [DataMember(Name = "is_playable")]
        public bool IsPlayable { get; set; }
        
        [DataMember(Name = "thumbnail")]
        public string Thumbnail { get; set; }
        
        [DataMember(Name = "icon")]
        public string Icon { get; set; }
        
        [DataMember(Name = "label2")]
        public string SecondLabel { get; set; }
    }
    
    [DataContract]
    public class IPTVMenuResponse
    {
        [DataMember(Name = "content_type")]
        public string ContentType { get; set; }
        
        [DataMember(Name = "items")]
        public List<IPTVMenuItemResponse> Items { get; set; }
    }
    
    [DataContract]
    public class IPTVMenuItemResponse
    {
        [DataMember(Name = "label")]
        public string Label { get; set; }
        
        [DataMember(Name = "path")]
        public string Path { get; set; }
        
        [DataMember(Name = "is_playable")]
        public bool IsPlayable { get; set; }
        
        [DataMember(Name = "thumbnail")]
        public string Thumbnail { get; set; }
        
        [DataMember(Name = "icon")]
        public string Icon { get; set; }
        
        [DataMember(Name = "label2")]
        public string SecondLabel { get; set; }
        
        [DataMember(Name = "stream_info")]
        public IPTVStreamInfoResponse StreamInfo { get; set; }
        
        [DataMember(Name = "info")]
        public IPTVMenuItemInfoResponse Info { get; set; }
    }
    
    [DataContract]
    public class IPTVMenuItemInfoResponse
    {
        [DataMember(Name = "duration")]
        public int Duration { get; set; }
        
        [DataMember(Name = "writer")]
        public string Writer { get; set; }
        
        [DataMember(Name = "title")]
        public string Title { get; set; }
        
        [DataMember(Name = "plotoutline")]
        public string PlotOutline { get; set; }
        
        [DataMember(Name = "plot")]
        public string Plot { get; set; }
        
        [DataMember(Name = "director")]
        public string Director { get; set; }
        
        [DataMember(Name = "rating")]
        public string Rating { get; set; }
        
        [DataMember(Name = "year")]
        public string Year { get; set; }
        
        [DataMember(Name = "genre")]
        public string Genre { get; set; }
    }

    [DataContract]
    public class IPTVStreamInfoResponse
    {
        [DataMember(Name = "audio")]
        public IPTVAudioInfoResponse Audio { get; set; }
        
        [DataMember(Name = "video")]
        public IPTVVideoInfoResponse Video { get; set; }
    }
    
    [DataContract]
    public class IPTVAudioInfoResponse
    {
        [DataMember(Name = "channels")]
        public string Channels { get; set; }
        
        [DataMember(Name = "language")]
        public string Language { get; set; }
        
        [DataMember(Name = "codec")]
        public string Codec { get; set; }
    }
    
    [DataContract]
    public class IPTVVideoInfoResponse
    {
        [DataMember(Name = "codec")]
        public string Codec { get; set; }
    }
    
    [DataContract]
    public class IPTVArtResponse
    {
        [DataMember(Name = "fanart")]
        public string FanArt { get; set; }
        
        [DataMember(Name = "poster")]
        public string Poster { get; set; }
        
        [DataMember(Name = "thumb")]
        public string Thumb { get; set; }
    }
    
    [DataContract]
    public class IPTVThreadResponse
    {
        [DataMember(Name = "content_type")]
        public string ContentType { get; set; }
        
        [DataMember(Name = "items")]
        public List<IPTVItemResponse> Items { get; set; }
    }
    
    [DataContract]
    public class IPTVItemResponse
    {
        [DataMember(Name = "label")]
        public string Label { get; set; }
        
        [DataMember(Name = "path")]
        public string Path { get; set; }
        
        [DataMember(Name = "is_playable")]
        public bool IsPlayable { get; set; }
        
        [DataMember(Name = "thumbnail")]
        public string Thumbnail { get; set; }
        
        [DataMember(Name = "icon")]
        public string Icon { get; set; }
        
        [DataMember(Name = "label2")]
        public string SecondLabel { get; set; }
    }

    [DataContract]
    public class IPTVStreamResponse
    {
        [DataMember(Name = "url")]
        public string Url { get; set; }
        
        [DataMember(Name = "subtitle")]
        public string Subtitle { get; set; }
    }
}