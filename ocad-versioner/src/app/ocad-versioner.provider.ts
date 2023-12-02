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
  private readonly ReleasesFolderName: string = 'Releases';
  async setFileHandleTree(
    projectDirectoryHandle: CustomFileSystemDirectoryHandle,
    currentOcdFileHandle: FileSystemFileHandle
  ): Promise<void> {
    this.fileHandleTree['current'] = { ocadFileHandle: currentOcdFileHandle };
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
    console.log('fileHandleTree: ', this.fileHandleTree);
  }
  getFeatureCollection(
    version: string
  ): FeatureCollection<Geometry, GeoJsonProperties> {
    throw new Error('Method not implemented.');
  }
  private fileHandleTree: IFileHandleTree = {};
}
