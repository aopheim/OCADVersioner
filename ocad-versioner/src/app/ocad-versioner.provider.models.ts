import { CustomFileSystemDirectoryHandle } from './customWindow';
import { OcadVersionListItemDto } from './components/ocad-version-list/ocad-version-list.models';
import { Observable } from 'rxjs';

export interface IOcadVersionerProvider {
  setFileHandleTree(
    projectDirectoryHandle: FileSystemDirectoryHandle,
    currentOcdFileHandle: FileSystemFileHandle
  ): Promise<void>;
  updateFileHandleTree(): Promise<void>;
  getOcdFileHandle(versionName: string): FileSystemFileHandle;
  getReleasesDirectoryHandle(): Promise<
    CustomFileSystemDirectoryHandle | undefined
  >;
  getCurrentFileName(): string;
  getNewVersionName(): string;
  getHighestVersionNumber(): number | null;
  versionMetaDataList$: Observable<OcadVersionListItemDto[]>;
}

export interface IFileHandleTree {
  [version: string]: IVersionFileHandle;
}

export interface IVersionFileHandle {
  ocadFileHandle: FileSystemFileHandle;
  metaDataFileHandle?: FileSystemFileHandle;
}
