// methods for the file system access api https://developer.mozilla.org/en-US/docs/Web/API/File_System_API are currently experimental and thus not available through the global Window type.
// Expanding the interface globally here to get correctly typing when accessing the Window object as suggested here: https://www.totaltypescript.com/how-to-properly-type-window
interface Window {
    showDirectoryPicker(
        options?: ShowDirectoryPickerOptions
    ): Promise<CustomFileSystemDirectoryHandle>
}

// These methods are currently not available on the FileSystemDirectoryHandle (probably because it is an experimental feature).
// Making a custom extension to get typings.
interface CustomFileSystemDirectoryHandle extends FileSystemDirectoryHandle {
    keys(): AsyncIterableIterator<string>
    getDirectoryHandle(
        name: string,
        options?: FileSystemGetDirectoryOptions | undefined
    ): Promise<CustomFileSystemDirectoryHandle>
}

export interface ShowDirectoryPickerOptions {
    id?: string
    mode?: 'read' | 'readwrite'
    startIn?:
        | 'desktop'
        | 'documents'
        | 'downloads'
        | 'music'
        | 'pictures'
        | 'videos'
}
