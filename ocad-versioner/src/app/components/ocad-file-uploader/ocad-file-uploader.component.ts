import { Component, EventEmitter, Inject, Output } from '@angular/core';
import { WINDOW } from '@ng-web-apis/common';
import * as ocad2geojson from 'ocad2geojson';
import { FeatureCollection } from 'geojson';
import {
  CustomFileSystemDirectoryHandle,
  ShowDirectoryPickerOptions,
  Window,
} from '../../customWindow';
import { from, map } from 'rxjs';
import { Buffer } from 'buffer';
import { OcadDirectoryHelper } from './ocad-file-uploader.helper';
import { isNil } from 'lodash-es';

@Component({
  selector: 'ocad-file-uploader',
  templateUrl: './ocad-file-uploader.component.html',
  styleUrl: './ocad-file-uploader.component.scss',
})
export class OcadFileUploaderComponent {
  private readonly ReleasesFolderName: string = 'Releases';
  public ocadFileNamesInDirectory: string[] = [];
  public versionFolderNames: string[] = [];
  public currentOcdFileHandle: FileSystemFileHandle | undefined;
  public oldOcdFileHandle: FileSystemFileHandle | undefined;
  public ocadDirectoryHandle: CustomFileSystemDirectoryHandle | null = null;
  public releasesDirectoryHandle: CustomFileSystemDirectoryHandle | null = null;
  public errorMessage: string = '';
  @Output() public ocadFileAsGeoJson = new EventEmitter<FeatureCollection>();
  @Output() public oldOcadFileAsGeoJson = new EventEmitter<FeatureCollection>();
  constructor(@Inject(WINDOW) private readonly window: Window) {}

  public async chooseOcadDirectory() {
    const options: ShowDirectoryPickerOptions = {
      mode: 'readwrite',
    };
    this.ocadDirectoryHandle = await this.window.showDirectoryPicker(options);
    for await (const fileOrDirectoryName of this.ocadDirectoryHandle.keys()) {
      if (fileOrDirectoryName.endsWith('.ocd'))
        this.ocadFileNamesInDirectory.push(fileOrDirectoryName);
    }
    if (this.ocadFileNamesInDirectory.length === 0) {
      // TODO: Fix translation using TranslateService
      this.errorMessage = `Found no .ocd file in the ${this.ocadDirectoryHandle.name} folder`;
      return;
    }
    if (this.ocadFileNamesInDirectory.length === 1) {
      this.currentOcdFileHandle = await this.ocadDirectoryHandle?.getFileHandle(
        this.ocadFileNamesInDirectory[0],
        {
          create: false,
        }
      );
      this.readOcdFile(
        await this.currentOcdFileHandle.getFile(),
        this.ocadFileAsGeoJson
      );
    }
    this.releasesDirectoryHandle =
      await this.ocadDirectoryHandle.getDirectoryHandle(
        this.ReleasesFolderName,
        {
          create: true,
        }
      );
    for await (const versionDirectoryName of this.releasesDirectoryHandle.keys()) {
      if (OcadDirectoryHelper.isValidReleaseFolderName(versionDirectoryName)) {
        this.versionFolderNames.push(versionDirectoryName);
      }
    }
  }

  public async setCurrentOcdFile(fileName: string) {
    this.currentOcdFileHandle = await this.ocadDirectoryHandle?.getFileHandle(
      fileName,
      {
        create: false,
      }
    );
    if (isNil(this.currentOcdFileHandle)) return;
    this.readOcdFile(
      await this.currentOcdFileHandle.getFile(),
      this.ocadFileAsGeoJson
    );
  }

  public async setOldOcdFile(versionName: string) {
    console.log('Setting ', versionName, ' as old file');
    const selectedReleaseDirectory =
      await this.releasesDirectoryHandle?.getDirectoryHandle(versionName, {
        create: false,
      });
    var oldOcdFileHandle = await selectedReleaseDirectory?.getFileHandle(
      `${versionName}.ocd`,
      { create: false }
    );

    if (!oldOcdFileHandle) {
      console.warn(
        'Found no file named ',
        versionName,
        '.ocd in folder ',
        versionName
      );
      return;
    }
    this.oldOcdFileHandle = oldOcdFileHandle;
    this.readOcdFile(
      await this.oldOcdFileHandle.getFile(),
      this.oldOcadFileAsGeoJson
    );
  }

  private async readOcdFile(
    file: File,
    emitter: EventEmitter<FeatureCollection>
  ): Promise<void> {
    var reader = new FileReader();
    reader.onload = (e) => {
      const arrayBuffer = reader.result;
      from(ocad2geojson.readOcad(Buffer.from(arrayBuffer as ArrayBuffer)))
        .pipe(
          map((ocadFile) => {
            console.log('reading ocad file');
            const geoJson = ocad2geojson.ocadToGeoJson(ocadFile);
            emitter.emit(geoJson);
          })
        )
        .subscribe();
    };
    if (!file) console.warn('Error reading ocd file');
    reader.readAsArrayBuffer(file as File);
  }
}
