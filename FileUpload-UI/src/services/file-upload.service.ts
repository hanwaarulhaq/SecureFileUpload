// services/file-upload.service.ts
import { Injectable } from '@angular/core';
import { HttpClient, HttpEvent, HttpEventType, HttpRequest, HttpResponse } from '@angular/common/http';
import { Observable, forkJoin, of } from 'rxjs';
import { catchError, map, mergeMap, tap } from 'rxjs/operators';
import * as CryptoJS from 'crypto-js';
import * as pako from 'pako';

@Injectable({
  providedIn: 'root'
})
export class FileUploadService {
  private apiUrl = 'http://localhost:50123/api/fileupload';

  constructor(private http: HttpClient) { }

 getUploadConfig(pageIdentifier: string): Observable<UploadConfig> {
    return this.http.get<UploadConfig>(`${this.apiUrl}/config/${pageIdentifier}`).pipe(
      catchError(error => {
        console.error('Error fetching upload config', error);
        // Return default values if API fails
        return of(this.getDefaultConfig(pageIdentifier));
      })
    );
  }

  private getDefaultConfig(pageIdentifier: string): UploadConfig {
    // Define default configurations for known page types
    const defaultConfigs: {[key: string]: UploadConfig} = {
      'profile-picture': {
        maxSize: 5 * 1024 * 1024, // 5MB
        allowedTypes: '.jpg,.png,.gif',
        allowedMimeTypes: 'image/jpeg,image/png,image/gif',
        //encrypt: false,
        //compress: true
      },
      'document-upload': {
        maxSize: 50 * 1024 * 1024, // 50MB
        allowedTypes: '.pdf,.doc,.docx,.xls,.xlsx',
        allowedMimeTypes: 'application/pdf,application/msword,application/vnd.openxmlformats-officedocument.wordprocessingml.document,application/vnd.ms-excel,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
        //encrypt: false,
        //compress: true
      },
      // Add more page types as needed
    };

    // Fallback to a generic config if page type not found
    return defaultConfigs[pageIdentifier] || {
      maxSize: 10 * 1024 * 1024, // 10MB default
      allowedTypes: '.jpg,.png,.pdf,.doc,.docx',
      allowedMimeTypes: 'image/jpeg,image/png,application/pdf,application/msword,application/vnd.openxmlformats-officedocument.wordprocessingml.document',
      //encrypt: true,
      //compress: true
    };
  }
  initiateUpload(fileInfo: any, pageIdentifier: string): Observable<any> {
    return this.http.post(`${this.apiUrl}/initiate`, {
      fileName: fileInfo.name,
      fileSize: fileInfo.size,
      fileType: fileInfo.type,
      //encrypt: encrypt,
      //compress: compress,
      pageType:pageIdentifier
    });
  }

 
  uploadChunk(session: any, chunk: Blob, chunkNumber: number, totalChunks: number): Observable<HttpEvent<any>> {
    const formData = new FormData();
    formData.append('file', chunk);
    formData.append('chunkNumber', chunkNumber.toString());
    //formData.append('totalChunks', totalChunks.toString());

    const req = new HttpRequest(
      'POST',
      `${this.apiUrl}/chunk/${session.sessionId}`,
      formData,
      {
        reportProgress: true
      }
    );

    return this.http.request(req).pipe(
      map(event => {
        if (event.type === HttpEventType.Response) {
          const body = event.body as { chunksUploaded: number; totalChunks: number };
          // Construct a proper HttpResponse object
          return new HttpResponse<any>({
            status: (event as HttpResponse<any>).status,
            body: body
          });
        }
        return event;
      })
    );
  }
  // completeUpload(session: any): Observable<any> {
  //   return this.http.post(`${this.apiUrl}/complete/${session.sessionId}`, session);
  // }
  completeUpload(sessionId: string, totalChunks: number): Observable<any> {
    //return this.http.post(`${this.apiUrl}/complete`, { sessionId, totalChunks });
    return this.http.post(`${this.apiUrl}/complete/${sessionId}`, { sessionId, totalChunks });
  }
  
  compressFile(file: Blob): Observable<Blob> {
    return new Observable(observer => {
      const reader = new FileReader();
      
      reader.onload = () => {
        try {
          const fileData = new Uint8Array(reader.result as ArrayBuffer);
          const compressed = pako.gzip(fileData);
          const compressedBlob = new Blob([compressed], { type: file.type });
          observer.next(compressedBlob);
          observer.complete();
        } catch (error) {
          observer.error(error);
        }
      };
      
      reader.onerror = error => observer.error(error);
      
      reader.readAsArrayBuffer(file);
    });
  }

  encryptFile(input: File | Blob, encryptionKey: string): Promise<Blob | File> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => {
      try {
        if (!encryptionKey || encryptionKey.trim() === "") {
          console.error("Encryption key is missing or invalid!");
          return;
        }

        const wordArray = CryptoJS.lib.WordArray.create(reader.result as ArrayBuffer);
        const parsedKey = CryptoJS.enc.Base64.parse(encryptionKey);
        const iv = CryptoJS.lib.WordArray.random(16);

        const encrypted = CryptoJS.AES.encrypt(wordArray, parsedKey, {
          mode: CryptoJS.mode.CBC,
          padding: CryptoJS.pad.Pkcs7,
          iv: iv
        });

        // Embed IV into encrypted data for later extraction
        const encryptedData = iv.toString(CryptoJS.enc.Base64) + ":" + encrypted.toString();

        const encryptedBlob = new Blob([encryptedData], { type: input.type });

        // Convert Blob back to File if needed
        const processedFile = input instanceof File 
          ? new File([encryptedBlob], input.name, { type: input.type, lastModified: input.lastModified }) 
          : encryptedBlob;

        resolve(processedFile);
      } catch (error) {
        reject(error);
      }
    };
    reader.onerror = reject;
    reader.readAsArrayBuffer(input);
  });
}

  
}

interface UploadConfig {
  maxSize: number;
  allowedTypes: string;
  allowedMimeTypes: string;
  chunkSize?: number;
  encrypt?: boolean;
  compress?: boolean;
}