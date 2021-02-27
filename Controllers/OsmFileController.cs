using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;

namespace OSMPbfWebAPI.Controllers
{
#pragma warning disable 1591
    /// <summary>
    /// OSM update mode
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum UpdateMode
    {
        None = 0,
        Minute = 1,
        Hour = 2,
        Day = 4
    }

    /// <summary>
    /// Update requset object used for API
    /// </summary>
    public record UpdateRequest(bool DownloadFile, bool UpdateFile);

    /// <summary>
    /// Create request object used for API
    /// </summary>
    public record CreateRequest(
            string Id,
            string FileName,
            string UpdateFileName,
            string OsmDownloadAddress,
            string OsmTimeStampAddress,
            string BaseUpdateAddress,
            UpdateMode UpdateMode
        );
#pragma warning restore 1591
    /// <summary>
    /// A controller to wrap OSM-C-Tools functionality
    /// </summary>
    [ApiController]
    [Route("")]
    public class OsmFileController : ControllerBase
    {
        private const string BASE_CONTAINERS_FOLDER = "containers";
        private const string OSM_UPDATE_PROCESS_NAME = "pyosmium-up-to-date";
        private const string OSM_CONVERT_PROCESS_NAME = "osmconvert";
        private const string JSON_CONFIG_FILE = "config.json";

        private readonly ILogger<OsmFileController> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IFileProvider _fileProvider;
        private readonly IWebHostEnvironment _webHostEnvironment;

        /// <summary>
        /// Class constructor
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="webHostEnvironment"></param>
        /// <param name="httpClientFactory"></param>
        public OsmFileController(
            ILogger<OsmFileController> logger,
            IWebHostEnvironment webHostEnvironment,
            IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _fileProvider = webHostEnvironment.ContentRootFileProvider;
            _webHostEnvironment = webHostEnvironment;
            _httpClientFactory = httpClientFactory;
        }

        /// <summary>
        /// Gets all the curently available OSM extracts
        /// </summary>
        /// <returns>A list of extract IDs</returns>
        [HttpGet]
        public string[] Get()
        {
            var directoryContent = _fileProvider.GetDirectoryContents(BASE_CONTAINERS_FOLDER);
            return directoryContent.Where(f => f.IsDirectory).Select(f => f.Name).ToArray();
        }

        /// <summary>
        /// Get an osm pbf file
        /// </summary>
        /// <param name="id">The ID of the extract</param>
        /// <returns>An OSM pbf file updated according to latest run of update method</returns>
        [HttpGet("{id}")]
        public async Task<IActionResult> Get(string id)
        {
            if (!_fileProvider.GetDirectoryContents(Path.Combine(BASE_CONTAINERS_FOLDER, id)).Any())
            {
                return BadRequest("There's a need to run POST create method before getting a file");
            }
            var config = await GetConfiguration(id);
            var fileInfo = _fileProvider.GetFileInfo(Path.Combine(BASE_CONTAINERS_FOLDER, id, config.FileName));
            return File(fileInfo.CreateReadStream(), "application/pbf", config.FileName);
        }

        /// <summary>
        /// Use this method to create an OSM extract 
        /// </summary>
        /// <param name="request">Example:
        /// { <br/>
        ///     "id": "an-id-to-use-for-future-comminucation", - optinal as this micro service can generate and return it<br/>
        ///     "fileName": "israel-and-palestine-latest.osm.pbf", <br/>
        ///     "updateFileName": "israel-and-palestine-updates.osc", <br/>
        ///     "osmDownloadAddress": "http://download.openstreetmap.fr/extracts/asia/israel_and_palestine-latest.osm.pbf", <br/>
        ///     "osmTimeStampAddress": "http://download.openstreetmap.fr/extracts/asia/israel_and_palestine.state.txt", <br/>
        ///     "baseUpdateAddress": "http://download.openstreetmap.fr/replication/asia/israel_and_palestine", <br/>
        ///     "updateMode": "Minute" <br/>
        /// }</param>
        /// <returns>The newly created extract ID</returns>
        [HttpPost]
        public async Task<string>CreateFolder(CreateRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Id))
            {
                request = request with { Id = Guid.NewGuid().ToString() };
            }
            
            var directoryFullPath = GetFullPathFromId(request.Id);
            if (_fileProvider.GetDirectoryContents(Path.Combine(BASE_CONTAINERS_FOLDER, request.Id)).Any())
            {
                _logger.LogInformation($"Directory already exists for id: {request.Id}, nothing to do");
                return request.Id;
            }
            var info = Directory.CreateDirectory(directoryFullPath);
            _logger.LogInformation($"Creating directory at: {directoryFullPath}");
            var requestString = JsonSerializer.Serialize(request);
            var configFileName = Path.Combine(info.FullName, JSON_CONFIG_FILE);
            _logger.LogInformation($"Writing config file at: {configFileName}");
            System.IO.File.WriteAllText(configFileName, requestString);
            await DownloadDailyOsmFile(request.Id, request);
            _logger.LogInformation($"Finished creating directory {request.Id}");
            return request.Id;
        }


        /// <summary>
        /// Update an osm extract by downloading a daily file and/or updating it
        /// </summary>
        /// <param name="id">The extract ID</param>
        /// <param name="request">The parameters to use for the update</param>
        /// <returns></returns>
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(string id, [FromBody]UpdateRequest request)
        {
            if (!_fileProvider.GetDirectoryContents(Path.Combine(BASE_CONTAINERS_FOLDER, id)).Any())
            {
                return BadRequest("There's a need to run POST create method before running this update");
            }
            var config = await GetConfiguration(id);
            if (request.DownloadFile)
            {
                await DownloadDailyOsmFile(id, config);
            }
            if (request.UpdateFile)
            {
                await UpdateFileToLatestVersion(id);
            }
            _logger.LogInformation("Finished OSM file manipulation.");
            return Ok();
        }

        private async Task DownloadDailyOsmFile(string id, CreateRequest config)
        {
            _logger.LogInformation("Starting downloading OSM file.");
            var (fileName, content) = await GetFileContent(config.OsmDownloadAddress);
            var osmFileFullPath = Path.Combine(GetFullPathFromId(id), fileName);
            _logger.LogInformation($"Saving OSM file to: {osmFileFullPath}");
            System.IO.File.WriteAllBytes(osmFileFullPath, content);

            if (!string.IsNullOrWhiteSpace(config.OsmTimeStampAddress))
            {
                // Update timestamp to match the one from the server.
                var file = await GetFileContent(config.OsmTimeStampAddress);
                var stringContent = Encoding.UTF8.GetString(file.content);
                var lastLine = stringContent.Split('\n').Last(s => !string.IsNullOrWhiteSpace(s));
                var timeStamp = lastLine.Split('=').Last().Replace("\\", "");
                RunOsmConvert($"--timestamp={timeStamp} {fileName}", GetFullPathFromId(id), config.FileName);
            }
        }

        private async Task UpdateFileToLatestVersion(string id)
        {
            _logger.LogInformation("Starting updating to latest OSM file.");
            var config = await GetConfiguration(id);
            var modeFlags = config.UpdateMode switch
            {
                UpdateMode.Day => "day",
                UpdateMode.Hour => "hour",
                UpdateMode.Minute => "minute",
                _ => throw new InvalidDataException(nameof(config.UpdateMode))
            };

            if (!RunProcess(OSM_UPDATE_PROCESS_NAME, $"--server \"{config.BaseUpdateAddress}/{modeFlags.Trim().ToLower()}/\" {config.FileName}", GetFullPathFromId(id), 60*60*1000))
            {
                throw new Exception("Failed to update to latest OSM file.");
            }
            _logger.LogInformation("Finished updating to latest OSM file.");
        }

        /// <summary>
        /// Update an extract with the given ID and returns the OSM change file content
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        [HttpPut("{id}/updates")]
        public async Task<IActionResult> GetUpdates(string id)
        {
            if (!_fileProvider.GetDirectoryContents(Path.Combine(BASE_CONTAINERS_FOLDER, id)).Any())
            {
                return BadRequest("There's a need to run POST create method before running this update");
            }
            var config = await GetConfiguration(id);
            await UpdateFileToLatestVersion(id);
            var fileInfo = _fileProvider.GetFileInfo(Path.Combine(BASE_CONTAINERS_FOLDER, id, config.UpdateFileName));
            return File(fileInfo.CreateReadStream(), "application/xml");
        }

        /// <summary>
        /// Deletes an extract 
        /// </summary>
        /// <param name="id">The extract ID</param>
        /// <returns></returns>
        [HttpDelete("{id}")]
        public IActionResult Delete(string id)
        {
            if (!_fileProvider.GetDirectoryContents(Path.Combine(BASE_CONTAINERS_FOLDER, id)).Any())
            {
                return BadRequest("There's a need to run POST create method before running delete");
            }
            Directory.Delete(GetFullPathFromId(id));
            return Ok();
        }

        private void RunOsmConvert(string parameters, string workingDirectory, string fileName)
        {
            var tempOsmFileName = $"temp-{fileName}";
            RunProcess(OSM_CONVERT_PROCESS_NAME, $"{parameters} -o={tempOsmFileName}", workingDirectory, 60*60*1000);
            System.IO.File.Move(Path.Combine(workingDirectory, tempOsmFileName), Path.Combine(workingDirectory, fileName), true);
        }

        private async Task<(string fileName, byte[] content)> GetFileContent(string url)
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromMinutes(60);
            var response = await client.GetAsync(url);
            var fileName = response.Content.Headers.ContentDisposition?.FileName?.Trim('"') ??
                response.Content.Headers.ContentDisposition?.FileNameStar?.Trim('"') ??
                url.Substring(url.LastIndexOf("/", StringComparison.Ordinal) + 1);
            var content = Array.Empty<byte>();
            if (response.IsSuccessStatusCode)
            {
                content = await response.Content.ReadAsByteArrayAsync();
            }
            else
            {
                _logger.LogError("Unable to retrieve file from: " + url + ", Status code: " + response.StatusCode);
            }

            return (fileName, content);
        }

        private bool RunProcess(string fileName, string arguments, string workingDirectory, int timeOutInMilliseconds)
        {
            _logger.LogInformation($"Starting process: {fileName} {arguments} at {workingDirectory}");
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    WorkingDirectory = workingDirectory,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                }
            };
            process.Start();
            process.WaitForExit(timeOutInMilliseconds);
            if (process.ExitCode != 0)
            {
                _logger.LogError($"Finished process {fileName} {arguments} without sucsess {process.ExitCode}");
                return false;
            }
            _logger.LogInformation($"Finished process {fileName} {arguments} successfully");
            return true;
        }

        private async Task<CreateRequest> GetConfiguration(string id)
        {
            var fileInfo = _fileProvider.GetFileInfo(Path.Combine(BASE_CONTAINERS_FOLDER, id, JSON_CONFIG_FILE));
            var stream = fileInfo.CreateReadStream();
            return await JsonSerializer.DeserializeAsync<CreateRequest>(stream);
        }

        private string GetFullPathFromId(string id)
        {
            return Path.Combine(_webHostEnvironment.ContentRootPath, BASE_CONTAINERS_FOLDER, id);
        }
    }
}
