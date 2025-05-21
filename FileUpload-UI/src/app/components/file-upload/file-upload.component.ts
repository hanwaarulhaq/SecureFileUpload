// components/file-upload/file-upload.component.ts
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { FileUploadService } from '../../../services/file-upload.service';
import { from, Observable, of, throwError } from 'rxjs';
import { catchError,concatMap, finalize, map, mergeMap, tap, last } from 'rxjs/operators';
import { HttpEventType } from '@angular/common/http';

interface FileTypeConfig {
  mime: string;
  magicNumbers: number[][];
}

@Component({
  selector: 'app-file-upload',
  templateUrl: './file-upload.component.html',
  styleUrls: ['./file-upload.component.scss']
})
export class FileUploadComponent {
  @Input() pageIdentifier: string = ''; // New input for page identification
  allowedTypes: string = ''; 
  allowedMimeTypes: string = '';
  maxSize: number = 0;
  chunkSize: number = 1 * 1024 * 1024; // Default chunk size
  encrypt: boolean = true;
  compress: boolean = true;

  @Output() uploadComplete = new EventEmitter<any>();
  @Output() uploadError = new EventEmitter<any>();
  @Output() progress = new EventEmitter<number>();

  selectedFile: File | null = null;
  isUploading: boolean = false;
  uploadProgress: number = 0;
  errorMessage: string = '';

  private fileTypeMap: { [key: string]: FileTypeConfig } = {
    'image/jpeg': { mime: 'image/jpeg', magicNumbers: [[0xFF, 0xD8, 0xFF, 0xE0], [0xFF, 0xD8, 0xFF, 0xE1], [0xFF, 0xD8, 0xFF, 0xEE]] },
    'image/png': { mime: 'image/png', magicNumbers: [[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]] },
    'image/gif': { mime: 'image/gif', magicNumbers: [[0x47, 0x49, 0x46, 0x38, 0x37, 0x61], [0x47, 0x49, 0x46, 0x38, 0x39, 0x61]] },
    'application/pdf': { mime: 'application/pdf', magicNumbers: [[0x25, 0x50, 0x44, 0x46]] },
    'application/msword': { mime: 'application/msword', magicNumbers: [[0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1]] }, // .doc
    'application/vnd.openxmlformats-officedocument.wordprocessingml.document': { mime: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document', magicNumbers: [[0x50, 0x4B, 0x03, 0x04]] }, // .docx
    'text/csv': { mime: 'text/csv', magicNumbers: [] }, // CSV is plain text, often no reliable magic number
    'application/vnd.ms-excel': { mime: 'application/vnd.ms-excel', magicNumbers: [[0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1]] }, // .xls (same as .doc for older formats)
    'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet': { mime: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', magicNumbers: [[0x50, 0x4B, 0x03, 0x04]] }, // .xlsx (same as .docx for zip-based format)
    'application/zip': { mime: 'application/zip', magicNumbers: [[0x50, 0x4B, 0x03, 0x04]] }, // Generic ZIP (can include .xlsx, .docx, etc.)
    'application/x-tar': { mime: 'application/x-tar', magicNumbers: [[0x75, 0x73, 0x74, 0x61, 0x72, 0x00, 0x30, 0x30], [0x75, 0x73, 0x74, 0x61, 0x72, 0x20, 0x20, 0x00]] }, // .tar
    'application/x-gzip': { mime: 'application/x-gzip', magicNumbers: [[0x1F, 0x8B]] }, // .gz
    'audio/mpeg': { mime: 'audio/mpeg', magicNumbers: [[0xFF, 0xFB], [0xFF, 0xF3], [0xFF, 0xF2]] }, // .mp3
    'audio/mp4': { mime: 'audio/mp4', magicNumbers: [[0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70, 0x6D, 0x70, 0x34]] }, // .mp4 (audio only)
    'video/mp4': { mime: 'video/mp4', magicNumbers: [[0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70, 0x6D, 0x70, 0x34]] }, // .mp4 (video)
    'video/x-msvideo': { mime: 'video/x-msvideo', magicNumbers: [[0x52, 0x49, 0x46, 0x46]] }, // .avi
    'application/json': { mime: 'application/json', magicNumbers: [] }, // JSON is plain text, often starts with '{' or '['
    'text/xml': { mime: 'text/xml', magicNumbers: [[0x3C, 0x3F, 0x78, 0x6D, 0x6C]] }, // XML often starts with '<?xml'
    'text/html': { mime: 'text/html', magicNumbers: [[0x3C, 0x21, 0x44, 0x4F, 0x43, 0x54, 0x59, 0x50, 0x45, 0x20, 0x48, 0x54, 0x4D, 0x4C]] }, // HTML often starts with '<!DOCTYPE HTML' or '<html>'
    'application/octet-stream': { mime: 'application/octet-stream', magicNumbers: [] }, // Generic binary data, often no reliable magic number
  };

  constructor(private fileUploadService: FileUploadService) { }
  ngOnInit(): void {
    if (!this.pageIdentifier) {
      console.error('FileUploadComponent: pageIdentifier is required');
      return;
    }
    this.loadUploadConfig(this.pageIdentifier);
  }
  private loadUploadConfig(pageIdentifier: string): void {
    this.fileUploadService.getUploadConfig(pageIdentifier).subscribe({
        next: (config) => {
            this.maxSize = config.maxSize;
            this.allowedTypes = config.allowedTypes;
            this.allowedMimeTypes = config.allowedMimeTypes;
            this.chunkSize = config.chunkSize || this.chunkSize;
            this.encrypt = config.encrypt !== undefined ? config.encrypt : this.encrypt;
            this.compress = config.compress !== undefined ? config.compress : this.compress;
        },
        error: (err) => {
            console.error('Error loading upload config', err);
            this.errorMessage = 'Failed to load upload configuration';
        }
    });
  }
  onFileSelected(event: any): void {
    const file = event.target.files[0];
    this.errorMessage = '';

    if (!file) {
      return;
    }

    // Validate file extension
    if (this.allowedTypes && !this.allowedTypes.split(',').map(type => type.trim().toLowerCase()).includes('.' + file.name.split('.').pop()!.toLowerCase())) {
      this.errorMessage = 'File type not allowed (extension)';
      return;
    }

    // Validate file size
    if (file.size > this.maxSize) {
      this.errorMessage = `File size exceeds maximum allowed size of ${this.maxSize / (1024 * 1024)}MB`;
      return;
    }

    // Validate file name
    if (!this.isValidFileName(file.name)) {
      this.errorMessage = 'Invalid file name';
      return;
    }

    this.selectedFile = file;

    // Optionally validate file header if allowedMimeTypes are provided
    if (this.allowedMimeTypes) {
      this.validateFileHeader(this.selectedFile, this.allowedMimeTypes.split(',').map(type => type.trim().toLowerCase())).pipe(
        catchError(error => {
          this.errorMessage = error;
          return of(false);
        }),
        tap(isValidHeader => {
          if (!isValidHeader) {
            this.errorMessage = 'File type not allowed (header)';
            this.selectedFile = null;
          }
        })
      ).subscribe();
    }
  }

  uploadFile(): void {
    if (!this.selectedFile) {
        return;
    }
    this.errorMessage = "";
    this.isUploading = true;
    this.uploadProgress = 0;

    let fileToUpload: File = this.selectedFile;  // Keep as File initially
    let processedFile: Blob | File = this.selectedFile;  // This will hold compressed/encrypted data   
    let currentSession: any;
    this.fileUploadService.initiateUpload(fileToUpload, this.pageIdentifier).pipe(
        tap(session => {
            currentSession = session;
        }),
        // Compression step
        mergeMap(session => {
            if (this.compress && session.isCompressed) {
                return this.fileUploadService.compressFile(processedFile).pipe(
                    map(compressedBlob => {
                        processedFile = compressedBlob;
                        currentSession.fileSize = compressedBlob.size;
                        return session;
                    })
                );
            }
            return of(session);
        }),
        // Encryption step
        mergeMap(session => {
          if (session.isEncrypted) {
            return from(this.fileUploadService.encryptFile(processedFile, session.encryptionKey)).pipe(
              tap(result => processedFile = result),
              map(() => session)
            );
          }
          return of(session);
        }),       
        // Chunk upload
        mergeMap(initialSession => {
          const totalChunks = Math.ceil(processedFile.size / this.chunkSize);
          initialSession.totalChunks = totalChunks;
          initialSession.chunksUploaded = 0;
          currentSession.totalChunks = initialSession.totalChunks;
          const chunkIndices = Array(totalChunks).fill(0).map((_, i) => i);
      
          return from(chunkIndices).pipe(
              concatMap(i => {
                  const start = i * this.chunkSize;
                  const end = Math.min(start + this.chunkSize, processedFile.size);
                  const chunk = processedFile.slice(start, end);
      
                  return this.fileUploadService.uploadChunk(
                      initialSession,
                      chunk,
                      i,
                      totalChunks
                  ).pipe(
                    tap(event => {
                      if (event.type === HttpEventType.UploadProgress) {
                          const chunkProgress = event.loaded / (event.total || 1);
                          const overallProgress = (i + chunkProgress) / totalChunks;
                          this.uploadProgress = Math.round(overallProgress * 100);
                          this.progress.emit(this.uploadProgress);
                      } else if (event.type === HttpEventType.Response) {
                          // Update the local session with the updated chunksUploaded from the backend
                          initialSession.chunksUploaded = event.body.chunksUploaded;
                          //initialSession.totalChunks = event.body.totalChunks; // Optionally update totalChunks if it can change 
                      }
                  }),
                      catchError(error => {
                          return throwError(error);
                      })
                  );
              }),
              last(),
              map(() => initialSession)
          );
      }),     
        // Complete upload
        mergeMap(updatedSessionFromChunks => { // Rename to reflect it's the updated session
        //  const completeSession = {
        //      sessionId: updatedSessionFromChunks.sessionId,
        //      fileName: updatedSessionFromChunks.fileName,
        //      fileSize: updatedSessionFromChunks.fileSize,
        //      fileType: updatedSessionFromChunks.fileType,
        //      totalChunks: updatedSessionFromChunks.totalChunks,
        //      chunksUploaded: updatedSessionFromChunks.chunksUploaded,
        //      isEncrypted: updatedSessionFromChunks.isEncrypted,
        //      isCompressed: updatedSessionFromChunks.isCompressed,
        //      encryptionKey: updatedSessionFromChunks.encryptionKey,
        //      //iv: updatedSessionFromChunks.iv
        //  };
         return this.fileUploadService.completeUpload(
           updatedSessionFromChunks.sessionId,
           currentSession.totalChunks
         );
         //return this.fileUploadService.completeUpload(completeSession);
        }),
      
        catchError(error => {
            this.errorMessage = error.error? 'Upload failed: ' + error.error:error.message;
            this.uploadError.emit(error);
            return throwError(error);
        }),
        finalize(() => {
            this.isUploading = false;
            this.selectedFile = null;
        })
    ).subscribe(
        result => {
            this.uploadComplete.emit(result);            
        }
    );
  }
  resetFileInput(): void {
    this.selectedFile = null;
    this.uploadProgress = 0;
    // This will reset the file input in the template
    const fileInput = document.getElementById('fileInput') as HTMLInputElement;
    if (fileInput) {
      fileInput.value = '';
    }
  }
  validateFileHeader(file: File, allowedMimeTypes: string[]): Observable<boolean> {
    return new Observable((observer) => {
      const fileReader = new FileReader();

      fileReader.onloadend = () => {
        const arrayBuffer = fileReader.result as ArrayBuffer;
        const uint8Array = new Uint8Array(arrayBuffer);

        for (const mimeType of allowedMimeTypes) {
          const config = this.fileTypeMap[mimeType];
          if (config) {
            for (const magicNumber of config.magicNumbers) {
              if (this.startsWith(uint8Array, magicNumber)) {
                observer.next(true);
                observer.complete();
                return;
              }
            }
          }
        }

        observer.next(false);
        observer.complete();
      };

      fileReader.onerror = () => {
        observer.error('Error reading file.');
      };

      fileReader.readAsArrayBuffer(file.slice(0, 8)); // Read the first 8 bytes (adjust as needed)
    });
  }

  private startsWith(array: Uint8Array, subarray: number[]): boolean {
    if (subarray.length > array.length) {
      return false;
    }
    for (let i = 0; i < subarray.length; i++) {
      if (array[i] !== subarray[i]) {
        return false;
      }
    }
    return true;
  }

  private isValidFileName(name: string): boolean {
    const invalidChars = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];
    const reservedNames = ['CON', 'PRN', 'AUX', 'NUL', 'COM1', 'COM2', 'COM3', 'COM4',
      'COM5', 'COM6', 'COM7', 'COM8', 'COM9', 'LPT1', 'LPT2',
      'LPT3', 'LPT4', 'LPT5', 'LPT6', 'LPT7', 'LPT8', 'LPT9'];

    // Check for invalid characters
    if (invalidChars.some(char => name.includes(char))) {
      return false;
    }

    // Check for reserved names
    let fileNameWithoutExt = '';
    const firstPart = name.split('.')[0];
    if (firstPart) {
      fileNameWithoutExt = firstPart.toUpperCase();
    }
    if (fileNameWithoutExt && reservedNames.includes(fileNameWithoutExt)) {
      return false;
    }

    // Check length
    if (name.length > 255) {
      return false;
    }

    return true;
  }
}
