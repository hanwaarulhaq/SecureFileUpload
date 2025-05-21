# SecureFileUpload
Secure File upload with Encryption, Compression and Chunks (Angular + Dotnet core  WEB API)
![Sequence Diagram](https://github.com/user-attachments/assets/61dc371e-bca5-4a29-873b-bf1c98dc9ff5)

# Workflow description of the file upload process based on the Angular component:
1. Initialization Phase
•	Trigger: Component loads with pageIdentifier input
•	Actions:
o	Fetches upload configuration from FileUploadService using pageIdentifier
o	Sets configuration:
	allowedTypes (file extensions)
	allowedMimeTypes
	maxSize (max file size)
	chunkSize (for chunked uploads)
	encrypt (boolean)
	compress (boolean)
•	Error Handling: Fails silently (logs error) if config loading fails
________________________________________
2. File Selection & Validation
•	Trigger: User selects a file via input
•	Validation Steps (in order):
1.	Extension Check:
	Compare file extensions against allowedTypes
	Rejects if extension is invalid
2.	Size Check:
	Verifies file.size ≤ maxSize
	Rejects with size error if too large
3.	Filename Check:
	Blocks reserved names (e.g., CON, LPT1)
	Rejects names with invalid chars (<>:"/\|?*)
4.	Header Validation (Optional):
	Reads file header bytes (magic numbers)
	Cross-checks with allowedMimeTypes
	Rejects if header doesn't match claimed type
•	Outcome:
o	If valid → Stores file in selectedFile
o	If invalid → Sets errorMessage
________________________________________
3. Upload Preparation
•	Trigger: uploadFile() called (e.g., via button click)
•	Steps:
1.	Session Initialization:
	Calls initiateUpload() to get:
	sessionId
	encryptionKey (if encryption enabled)
	isCompressed/isEncrypted flags
2.	File Processing Pipeline:
	Compression (if enabled):
	Runs compressFile() → Reduces file size
	Encryption (if enabled):
	Encrypt file/blob using encryptionKey
3.	Chunking:
	Splits processed file into chunks of chunkSize
	Calculates totalChunks = ceil(fileSize / chunkSize)
________________________________________
4. Chunked Upload Process
•	Execution:
o	Uploads chunks sequentially (not parallel):
1.	For each chunk (0 to N-1):
	Slices file portion: chunk = file.slice(start, end)
	Calls uploadChunk() with:
	Current session data
	Chunk index
	Total chunks count
2.	Tracks progress:
	Emits progress events (% uploaded)
	Updates chunksUploaded on server response
•	Error Handling:
o	Aborts remaining chunks on failure
o	Emits uploadError with details
________________________________________
5. Finalization
•	Trigger: All chunks uploaded successfully
•	Actions:
1.	Calls completeUpload() with final session data:
	Confirms all chunks received server-side
	Commits file assembly
2.	Emits uploadComplete event with:
	Server response (e.g., file URL, metadata)
•	Cleanup:
o	Resets isUploading, selectedFile
o	Sets uploadProgress = 100%
________________________________________
Error Handling Flow
•	Possible Failures:
o	Network errors during chunk upload
o	Server-side validation failures
o	Processing errors (compression/encryption)
•	Recovery:
o	No automatic retries (user must restart)
o	Error details emitted via uploadError
o	User-friendly errorMessage displayed
________________________________________
Key Features
•	Security: Header validation prevents fake extensions
•	Performance: Compression reduces bandwidth
•	Progress Tracking: Real-time % updates via progress emitter
 
# Workflow description of the backend file upload process, covering both the controller and service layers:
1. Initial Configuration Phase
Endpoint: GET api/fileupload/config/{pageType}
Flow:
1.	Controller (FileUploadController.GetUploadConfig):
o	Receives pageType (e.g., "picture-upload", "document-upload")
o	Calls FileUploadService.GetUploadConfig()
2.	Service (FileUploadService.GetUploadConfig):
o	Returns configuration based on pageType:
	MaxSize: Max file size (e.g., 5MB for images)
	AllowedTypes: Permitted extensions (e.g., ".jpg,.png")
	AllowedMimeTypes: Valid content types (e.g., "image/jpeg")
	ChunkSize: Chunk size (default: 1MB)
	Encrypt/Compress: Flags for processing
________________________________________
2. Session Initiation
Endpoint: POST api/fileupload/initiate
Request Body: InitiateUploadRequest (fileName, size, type, etc.)
Flow:
1.	Validation:
o	Checks file size against MaxSize (rejects if too large)
2.	Service (InitiateUploadSession):
o	Sanitizes filename (removes invalid chars, checks for homoglyphs)
o	Generates:
	sessionId (GUID)
	encryptionKey (if encryption enabled)
	Temp file path (/TempUploads/{sessionId}.tmp)
o	Stores session in memory (_sessions list)
3.	Response: Returns session details (ID, total chunks, etc.)
________________________________________
3. Chunk Processing
Endpoint: POST api/fileupload/chunk/{sessionId}
Form Data: Chunk file + chunkNumber + totalChunks
Flow:
1.	Controller:
o	Validates session exists via GetSession(sessionId)
2.	Service (ProcessChunk):
o	Saves chunk to temp file (/TempUploads/{sessionId}_{chunkNumber}.part)
o	Updates session's chunksUploaded count
3.	Error Handling:
o	On failure: Deletes all temp files for the session
o	Returns HTTP 400 with error details
________________________________________
4. Upload Completion
Endpoint: POST api/fileupload/complete/{sessionId}
Flow:
1.	Validation (ValidateUploadCompletion):
o	Verifies all chunks were uploaded (chunksUploaded == totalChunks)
o	Checks for double extensions (e.g., "malicious.txt.exe")
2.	File Assembly (CombineChunksAsync):
o	Merges chunks into single temp file in sequence
o	Deletes individual chunk files
3.	Post-Processing:
o	Decryption: Uses AES-CBC with stored key (if IsEncrypted)
o	Decompression: Uses GZIP (if IsCompressed)
4.	Virus Scan (ScanVirus):
o	Scans file using Windows Defender (if service running)
o	Rejects if malware detected
5.	Final Save:
o	Moves file to permanent storage (/Uploads/{guid}.ext)
o	Returns to the final file path
________________________________________
5. Cleanup
•	Temp files deleted after successful completion or on error
•	Session remains in memory (could be extended to database storage)
________________________________________
Key Security Features
1.	File Validation:
o	Header Verification: Checks magic numbers against MIME type
o	Extension Whitelisting: Only allows configured extensions
o	Size Limits: Enforces MaxSize per file type
2.	Name Sanitization:
o	Blocks Unicode homoglyphs (e.g., Cyrillic 'а' vs Latin 'a')
o	Removes zero-width characters
o	Rejects bidirectional control chars (RLO/LRO)
3.	Processing:
o	Encryption (AES-256) with unique per-file key
o	Compression (GZIP) to reduce storage
4.	Antivirus: Windows Defender integration
![BackEnd Process](https://github.com/user-attachments/assets/f60f9768-e3e3-47fb-b8d4-49fdd7db5b28)



 

