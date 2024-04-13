import {
  AfterViewInit,
  Component,
  ElementRef,
  EventEmitter,
  Inject,
  OnDestroy,
  Output,
  ViewChild,
} from '@angular/core';
import { WINDOW } from '@ng-web-apis/common';
import {
  CustomFileSystemDirectoryHandle,
  ShowDirectoryPickerOptions,
  Window,
} from '../../customWindow';
import { OcadVersionerProvider } from '../../ocad-versioner.provider';
import { isNil } from 'lodash-es';

@Component({
  selector: 'project-directory-selector',
  templateUrl: './project-directory-selector.component.html',
  styleUrl: './project-directory-selector.component.scss',
})
export class ProjectDirectorySelectorComponent
  implements AfterViewInit, OnDestroy
{
  private projectDirectoryHandle: CustomFileSystemDirectoryHandle | null = null;
  public ocadFileNamesInProjectDirectory: string[] = [];
  public errorMessages: [DirectorySelectorErrorTypes, string][] = [];
  public DirectorySelectorErrorTypes = DirectorySelectorErrorTypes;
  @Output()
  public projectLoaded: EventEmitter<boolean> = new EventEmitter<boolean>();
  @ViewChild('openModalButton') openModalButton: ElementRef | null = null;
  @ViewChild('closeModalButton') closeModalButton: ElementRef | null = null;

  constructor(
    @Inject(WINDOW) private readonly window: Window,
    private ocadVersionerProvider: OcadVersionerProvider
  ) {
    this.projectDirectoryHandle = null;
    if (isNil(this.window.showDirectoryPicker))
      this.errorMessages.push([
        DirectorySelectorErrorTypes.UnsupportedBrowser,
        this.getErrorMessageForErrorType(
          DirectorySelectorErrorTypes.UnsupportedBrowser
        ),
      ]);
  }
  ngOnDestroy(): void {
    this.openModalButton?.nativeElement.click();
  }

  ngAfterViewInit(): void {
    // Found no other way to trigger the modal without using jQuery...
    this.openModalButton?.nativeElement.click();
  }

  public async setProjectDirectory() {
    const directoryPickerOptions: ShowDirectoryPickerOptions = {
      mode: 'readwrite',
    };
    this.projectDirectoryHandle = await this.window.showDirectoryPicker(
      directoryPickerOptions
    );
    for await (const fileOrDirectoryName of this.projectDirectoryHandle.keys()) {
      if (
        fileOrDirectoryName.endsWith('.ocd') &&
        !this.ocadFileNamesInProjectDirectory.includes(fileOrDirectoryName)
      )
        this.ocadFileNamesInProjectDirectory.push(fileOrDirectoryName);
    }
    if (this.ocadFileNamesInProjectDirectory.length === 0) {
      this.errorMessages.push([
        DirectorySelectorErrorTypes.MissingOcdFileInProjectDirectory,
        this.getErrorMessageForErrorType(
          DirectorySelectorErrorTypes.MissingOcdFileInProjectDirectory
        ),
      ]);
      return;
    }
    if (this.ocadFileNamesInProjectDirectory.length === 1) {
      const currentOcdFileHandle =
        await this.projectDirectoryHandle?.getFileHandle(
          this.ocadFileNamesInProjectDirectory[0],
          {
            create: false,
          }
        );
      if (!currentOcdFileHandle) {
        this.errorMessages.push([
          DirectorySelectorErrorTypes.CouldNotReadOcadFile,
          `Could not read .ocd file ${this.ocadFileNamesInProjectDirectory[0]}`,
        ]);
        return;
      }
      await this.setFileHandleTree(
        this.projectDirectoryHandle,
        currentOcdFileHandle
      );
    }
  }

  public async setCurrentOcdFile(fileName: string) {
    if (!this.projectDirectoryHandle) {
      console.warn('projectDirectoryHandle is null');
      return;
    }
    const currentOcdFileHandle =
      await this.projectDirectoryHandle?.getFileHandle(fileName, {
        create: false,
      });
    if (!currentOcdFileHandle) {
      this.errorMessages.push([
        DirectorySelectorErrorTypes.CouldNotReadOcadFile,
        `Could not read .ocd file ${fileName}`,
      ]);
      return;
    }
    if (currentOcdFileHandle && this.projectDirectoryHandle)
      await this.setFileHandleTree(
        this.projectDirectoryHandle,
        currentOcdFileHandle
      );
  }

  private async setFileHandleTree(
    projectDirectoryHandle: CustomFileSystemDirectoryHandle,
    currentOcdFileHandle: FileSystemFileHandle
  ): Promise<void> {
    await this.ocadVersionerProvider.setFileHandleTree(
      projectDirectoryHandle,
      currentOcdFileHandle
    );
    // This should be rewritten. setFileHandleTree is being called twice. Use observables?
    await this.ocadVersionerProvider.updateFileHandleTree();
    // Setting to null to ensure that the component do not keep a reference to the handle. That is the job of the provider.
    this.projectDirectoryHandle = null;
    this.closeModalButton?.nativeElement.click();
    this.projectLoaded.emit(true);
    return;
  }

  private getErrorMessageForErrorType(
    errorType: DirectorySelectorErrorTypes
  ): string {
    switch (errorType) {
      case DirectorySelectorErrorTypes.UnsupportedBrowser:
        return '#_errorTypeUnsupportedBrowser';
      case DirectorySelectorErrorTypes.MissingOcdFileInProjectDirectory:
        // TODO: Find a smart way to do translations
        return `Found no .ocd file in the ${this.projectDirectoryHandle?.name} folder`;
      default:
        return '';
    }
  }
}

enum DirectorySelectorErrorTypes {
  UnsupportedBrowser = 1,
  MissingOcdFileInProjectDirectory = 2,
  CouldNotReadOcadFile = 3,
}
