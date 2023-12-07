import { Injectable } from '@angular/core';
import { IOcadFileSystemService } from './ocad-file-system-service.models';
import { OcadVersionListItemDto } from '../components/ocad-version-list/ocad-version-list.models';
import { OcadVersionMetaData } from '../components/create-version/create-version.models';
import { OcadDirectoryHelper } from '../components/project-directory-selector/project-directory-selector.helper';
import { CustomFileSystemDirectoryHandle } from '../customWindow';

@Injectable()
export class OcadFileSystemService implements IOcadFileSystemService {
  public async fileExistsInFolder(
    fileName: string,
    directoryHandle: CustomFileSystemDirectoryHandle
  ): Promise<boolean> {
    const fileHandle = await directoryHandle.getFileHandle(fileName, {
      create: true,
    });
    const isEmpty = (await fileHandle.getFile()).size <= 1;
    if (isEmpty) {
      directoryHandle.removeEntry(fileName, { recursive: false });
    }
    return !isEmpty;
  }

  // Added as a layer between saved model (OcadVersionMetaData) and dto object (OcadVersionListItemDto) to simplify mapping between models if they are changing.
  public async setJsonMetaDataFileToReleaseFolder(
    metaDataDto: OcadVersionListItemDto,
    releaseDirectoryHandle: FileSystemDirectoryHandle
  ): Promise<void> {
    const toSave: OcadVersionMetaData = {
      numberOfAddedSymbols: metaDataDto.numberOfAddedSymbols,
      numberOfDeletedSymbols: metaDataDto.numberOfDeletedSymbols,
      numberOfEditedSymbols: metaDataDto.numberOfEditedSymbols,
      versionCreatedAt: metaDataDto.versionCreatedAt,
      versionNumber: metaDataDto.versionNumber,
      description: metaDataDto.description,
      title: metaDataDto.title,
    };
    const json = JSON.stringify(toSave);
    const versionName = OcadDirectoryHelper.getVersionNameFromVersionNumber(
      metaDataDto.versionNumber
    );
    const newReleaseFolder = await releaseDirectoryHandle.getDirectoryHandle(
      versionName,
      { create: true }
    );
    const jsonFileHandle = await newReleaseFolder.getFileHandle(
      OcadDirectoryHelper.getJsonMetadataFileName(versionName),
      { create: true }
    );
    if (!jsonFileHandle) {
      console.warn(
        `Could not create json file handle ${OcadDirectoryHelper.getJsonMetadataFileName(
          versionName
        )}`
      );
      return;
    }
    const stream = await jsonFileHandle.createWritable({
      keepExistingData: false,
    });
    await stream.write(json);
    await stream.close();
  }

  public async copyOcdFileToNewReleaseFolder(
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
