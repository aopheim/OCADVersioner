import { FeatureCollection } from 'geojson';

export interface IOcadVersionerProvider {
  setFileHandleTree(
    projectDirectoryHandle: FileSystemDirectoryHandle,
    currentOcdFileHandle: FileSystemFileHandle
  ): Promise<void>;
  getFeatureCollection(version: string): FeatureCollection;
}

export interface IFileHandleTree {
  [version: string]: IVersionFileHandle;
}

export interface IVersionFileHandle {
  ocadFileHandle: FileSystemFileHandle;
}
