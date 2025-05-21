// app.component.ts
import { Component } from '@angular/core';

@Component({
  selector: 'app-root',
  templateUrl: './app.component.html',
  styleUrls: ['./app.component.scss']
})
export class AppComponent {
  title = 'Secure File Upload Demo';
  
  onUploadComplete(event: any) {
    console.log('Upload complete:', event);
    alert(`File uploaded successfully! Path: ${event.filePath}`);
  }
  
  onUploadError(error: any) {
    console.log('Upload error:', JSON.stringify(error));
    alert(`Upload failed: ${error.error}`);
  }
  
  onProgress(progress: number) {
    console.log('Upload progress:', progress);
  }
}