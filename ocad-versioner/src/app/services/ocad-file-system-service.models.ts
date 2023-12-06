export interface IOcadFileSystemService {
  copyFileToNewReleaseFolder(
    releaseName: string,
    ocdFileHandle: FileSystemFileHandle,
    releasesDirectoryHandle: FileSystemDirectoryHandle
  ): Promise<void>;
}
