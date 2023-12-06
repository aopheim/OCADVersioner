import { FeatureCollection } from 'geojson';
import { CustomFileSystemDirectoryHandle } from './customWindow';

export interface IOcadVersionerProvider {
  setFileHandleTree(
    projectDirectoryHandle: FileSystemDirectoryHandle,
    currentOcdFileHandle: FileSystemFileHandle
  ): Promise<void>;
  updateFileHandleTree(): Promise<void>;
  getFeatureCollection(version: string): FeatureCollection;
  getOcdFileHandle(versionName: string): FileSystemFileHandle;
  getReleasesDirectoryHandle(): Promise<
    CustomFileSystemDirectoryHandle | undefined
  >;
  getCurrentFileName(): string;
  getNewVersionName(): string;
}

export interface IFileHandleTree {
  [version: string]: IVersionFileHandle;
}

export interface IVersionFileHandle {
  ocadFileHandle: FileSystemFileHandle;
}
