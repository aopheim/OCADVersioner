import { Injectable } from '@angular/core';
import {
  IFileHandleTree,
  IOcadVersionerProvider,
} from './ocad-versioner.provider.models';
import { FeatureCollection, Geometry, GeoJsonProperties } from 'geojson';
import { CustomFileSystemDirectoryHandle } from './customWindow';
import { OcadDirectoryHelper } from './components/project-directory-selector/project-directory-selector.helper';

@Injectable()
export class OcadVersionerProvider implements IOcadVersionerProvider {
  private fileHandleTree: IFileHandleTree = {};
  private projectDirectoryHandle: CustomFileSystemDirectoryHandle | null = null;
  private currentOcdFileHandle: FileSystemFileHandle | null = null;
  private readonly ReleasesFolderName: string = 'Releases';

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

  getNewVersionName(): string {
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
    return await this.setFileHandleTree(
      this.projectDirectoryHandle,
      this.currentOcdFileHandle
    );
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
      const ocdFileHandle = await releaseDirectory.getFileHandle(
        `${versionDirectoryName}.ocd`,
        { create: false }
      );
      if (!ocdFileHandle) {
        console.warn(
          'Found no file named ',
          versionDirectoryName,
          '.ocd in folder ',
          versionDirectoryName
        );
        continue;
      }
      this.fileHandleTree[versionDirectoryName] = {
        ocadFileHandle: ocdFileHandle,
      };
    }
  }
  public getFeatureCollection(
    version: string
  ): FeatureCollection<Geometry, GeoJsonProperties> {
    throw new Error('Method not implemented.');
  }
}
