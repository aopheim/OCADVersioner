import { Injectable } from '@angular/core';
import {
  IFileHandleTree,
  IOcadVersionerProvider,
} from './ocad-versioner.provider.models';
import { CustomFileSystemDirectoryHandle } from './customWindow';
import { OcadDirectoryHelper } from './components/project-directory-selector/project-directory-selector.helper';
import { OcadVersionListItemDto } from './components/ocad-version-list/ocad-version-list.models';
import { OcadVersionMetaData } from './components/create-version/create-version.models';
import { OcadFileSystemService } from './services/ocad-file-system-service';
import { BehaviorSubject } from 'rxjs';
import { orderBy } from 'lodash-es';

@Injectable()
export class OcadVersionerProvider implements IOcadVersionerProvider {
  private fileHandleTree: IFileHandleTree = {};
  private projectDirectoryHandle: CustomFileSystemDirectoryHandle | null = null;
  private currentOcdFileHandle: FileSystemFileHandle | null = null;
  private readonly ReleasesFolderName: string = 'Releases';

  constructor(private fileSystemService: OcadFileSystemService) {}
  // Returns null if no versions are created
  public getHighestVersionNumber(): number | null {
    const versionNames = Object.keys(this.fileHandleTree).filter(
      (k) => k !== 'current'
    );
    const versionNumbers: number[] = versionNames
      .map((name) => OcadDirectoryHelper.getVersionNumberFromVersionName(name))
      .filter((n) => !!n) as number[];
    if (versionNumbers.length === 0) return null;
    return Math.max(...versionNumbers);
  }
  versionMetaDataList$: BehaviorSubject<OcadVersionListItemDto[]> =
    new BehaviorSubject<OcadVersionListItemDto[]>([]);

  private getCurrentNumberOfReleases(): number {
    return Object.keys(this.fileHandleTree || {}).filter(
      (k) => k !== 'current' && OcadDirectoryHelper.isValidReleaseFolderName(k)
    ).length;
  }

  public async getReleasesDirectoryHandle(): Promise<
    CustomFileSystemDirectoryHandle | undefined
  > {
    return await this.projectDirectoryHandle?.getDirectoryHandle(
      this.ReleasesFolderName
    );
  }

  public getOcdFileHandle(versionName: string): FileSystemFileHandle {
    return this.fileHandleTree[versionName]?.ocadFileHandle;
  }

  public getNewVersionName(): string {
    const newVersionNumber = this.getCurrentNumberOfReleases() + 1;
    return `V${newVersionNumber}`;
  }

  public getCurrentFileName(): string {
    return this.getOcdFileHandle('current')?.name ?? '';
  }

  public async updateFileHandleTree(): Promise<void> {
    if (!this.projectDirectoryHandle || !this.currentOcdFileHandle) {
      console.warn(
        'Can not update - project directory or current file not set'
      );
      return;
    }
    await this.setFileHandleTree(
      this.projectDirectoryHandle,
      this.currentOcdFileHandle
    );
    await this.setMetaDataListOfVersions();
  }

  public async setFileHandleTree(
    projectDirectoryHandle: CustomFileSystemDirectoryHandle,
    currentOcdFileHandle: FileSystemFileHandle
  ): Promise<void> {
    this.fileHandleTree['current'] = { ocadFileHandle: currentOcdFileHandle };
    this.currentOcdFileHandle = currentOcdFileHandle;
    this.projectDirectoryHandle = projectDirectoryHandle;
    const releasesDirectoryHandle =
      await projectDirectoryHandle.getDirectoryHandle(this.ReleasesFolderName, {
        create: true,
      });
    for await (const versionDirectoryName of releasesDirectoryHandle.keys()) {
      if (!OcadDirectoryHelper.isValidReleaseFolderName(versionDirectoryName))
        continue;
      const releaseDirectory = await releasesDirectoryHandle.getDirectoryHandle(
        versionDirectoryName,
        { create: false }
      );
      const ocadFileName =
        OcadDirectoryHelper.getOcdReleaseFileName(versionDirectoryName);
      if (
        !(await this.fileSystemService.fileExistsInFolder(
          ocadFileName,
          releaseDirectory
        ))
      ) {
        console.warn(
          'Found no file named ',
          ocadFileName,
          'in folder ',
          versionDirectoryName
        );
        continue;
      }
      const ocdFileHandle = await releaseDirectory.getFileHandle(ocadFileName, {
        create: false,
      });
      const jsonMetaDataFileName =
        OcadDirectoryHelper.getJsonMetadataFileName(versionDirectoryName);
      if (
        !(await this.fileSystemService.fileExistsInFolder(
          jsonMetaDataFileName,
          releaseDirectory
        ))
      ) {
        console.warn(
          'Found no json file named ',
          jsonMetaDataFileName,
          ' in folder ',
          versionDirectoryName
        );
        continue;
      }
      const jsonMetaDataFileHandle = await releaseDirectory.getFileHandle(
        OcadDirectoryHelper.getJsonMetadataFileName(versionDirectoryName),
        { create: false }
      );
      this.fileHandleTree[versionDirectoryName] = {
        ocadFileHandle: ocdFileHandle,
        metaDataFileHandle: jsonMetaDataFileHandle,
      };
    }
  }

  private async setMetaDataListOfVersions(): Promise<void> {
    const versionNames = Object.keys(this.fileHandleTree || {}).filter(
      (versionName) => OcadDirectoryHelper.isValidReleaseFolderName(versionName)
    );
    let dtos: OcadVersionListItemDto[] = [];
    for await (let versionName of versionNames) {
      const jsonFile = this.fileHandleTree[versionName]?.metaDataFileHandle;
      if (!jsonFile) {
        console.warn(
          'Found no json metadata file for version ',
          versionName,
          'Skipping'
        );
      } else {
        var fileAsText = await (await jsonFile.getFile()).text();
        const metaData: OcadVersionMetaData = JSON.parse(fileAsText);
        dtos.push({
          numberOfAddedSymbols: metaData.numberOfAddedSymbols,
          numberOfDeletedSymbols: metaData.numberOfDeletedSymbols,
          numberOfEditedSymbols: metaData.numberOfEditedSymbols,
          versionCreatedAt: metaData.versionCreatedAt,
          versionNumber: metaData.versionNumber,
          description: metaData.description,
          title: metaData.title,
        } as OcadVersionListItemDto);
      }
    }
    dtos = orderBy(
      dtos.filter((i) => !!i),
      (d) => d.versionCreatedAt,
      'desc'
    );

    this.versionMetaDataList$.next(dtos);
  }
}
