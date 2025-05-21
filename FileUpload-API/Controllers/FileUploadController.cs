using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using WebApplication.Models;
using WebApplication.Services;

namespace WebApplication.Controllers
{
   
    // Controllers/FileUploadController.cs
    [ApiController]
    [Route("api/[controller]")]
    public class FileUploadController : ControllerBase
    {
        private readonly FileUploadService _fileUploadService;
        private readonly ILogger<FileUploadController> _logger;

        public FileUploadController(FileUploadService fileUploadService, ILogger<FileUploadController> logger)
        {
            _fileUploadService = fileUploadService;
            _logger = logger;
        }
       

        [HttpGet("config/{pageType}")]
        [ProducesResponseType(typeof(UploadConfiguration), 200)]
        public IActionResult GetUploadConfig(string pageType)
        {
            try
            {
                var config = _fileUploadService.GetUploadConfig(pageType);
                return Ok(config);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting upload config");
                return BadRequest(ex.Message);
            }
        }

        [HttpPost("initiate")]
        public async Task<IActionResult> InitiateUpload([FromBody] InitiateUploadRequest request)
        {
            try
            {
                var session = await _fileUploadService.InitiateUploadSession(request); 
                return Ok(session);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initiating upload");
                return BadRequest(ex.Message);
            }
        }

        [HttpPost("chunk/{sessionId}")]
        [DisableRequestSizeLimit]
        public async Task<IActionResult> UploadChunk(string sessionId, [FromForm] IFormFile file, [FromForm] int chunkNumber)
        {
            try
            {
                await _fileUploadService.ProcessChunk(sessionId, file, chunkNumber);
                return Ok(new { ChunkNumber = chunkNumber, Status = "Uploaded" });
            }
            catch (Exception ex)
            {
                _fileUploadService.CleanTempFile(sessionId);
                _logger.LogError(ex, "Error uploading chunk");
                return BadRequest(ex.Message);
            }
        }

        [HttpPost("complete/{sessionId}")]
        //public async Task<IActionResult> CompleteUpload(string sessionId, [FromBody] int totalChunks)
        public async Task<IActionResult> CompleteUpload([FromBody] CompleteRequest req)
        {
            try
            {
                // In a real app, you'd retrieve the session from a store/database
                var session = _fileUploadService.GetSession(req.SessionId);
                if (session == null)
                {
                    return NotFound("Session not found");
                }
                session.TotalChunks = req.TotalChunks;
                var filePath = await _fileUploadService.CompleteUpload(session);
                return Ok(new { FilePath = filePath });
            }
            catch (Exception ex)
            {
                _fileUploadService.CleanTempFile(req.SessionId);
                _logger.LogError(ex, "Error completing upload");
                return BadRequest(ex.Message);
            }
        }
    }
}
