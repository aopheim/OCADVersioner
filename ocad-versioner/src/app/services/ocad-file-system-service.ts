import { Injectable } from '@angular/core';
import { IOcadFileSystemService } from './ocad-file-system-service.models';

@Injectable()
export class OcadFileSystemService implements IOcadFileSystemService {
  async copyFileToNewReleaseFolder(
    releaseName: string,
    ocdFileHandle: FileSystemFileHandle,
    releasesDirectoryHandle: FileSystemDirectoryHandle
  ): Promise<void> {
    const newReleaseFolder = await releasesDirectoryHandle.getDirectoryHandle(
      releaseName,
      {
        create: true,
      }
    );
    const ocdRelaseFileHandle = await newReleaseFolder.getFileHandle(
      `${releaseName}.ocd`,
      { create: true }
    );
    const writeableStream = await ocdRelaseFileHandle.createWritable({
      keepExistingData: false,
    });
    await writeableStream.write(await ocdFileHandle.getFile());
    await writeableStream.close();
  }
}
