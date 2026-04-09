import { OcadVersionListItemDto } from '../../components/ocad-version-list/ocad-version-list.models'
import { CustomFileSystemDirectoryHandle } from '../../customWindow'

export interface IOcadFileSystemService {
    copyOcdFileToNewReleaseFolder(
        releaseName: string,
        ocdFileHandle: FileSystemFileHandle,
        releasesDirectoryHandle: FileSystemDirectoryHandle
    ): Promise<void>

    setJsonMetaDataFileToReleaseFolder(
        metaDataDto: OcadVersionListItemDto,
        releasesDirectoryHandle: FileSystemDirectoryHandle
    ): Promise<void>

    fileExistsInFolder(
        fileName: string,
        directoryHandle: CustomFileSystemDirectoryHandle
    ): Promise<boolean>
}
