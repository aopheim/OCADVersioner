import { Component, Input } from '@angular/core';
import { FormControl, FormGroup, Validators } from '@angular/forms';
import { OcadFileSystemService } from '../../services/ocad-file-system-service/ocad-file-system-service';
import { OcadVersionerProvider } from '../../ocad-versioner.provider';
import { OcadDirectoryHelper } from '../project-directory-selector/project-directory-selector.helper';
import { OcadVersionListItemDto } from '../ocad-version-list/ocad-version-list.models';

@Component({
  selector: 'create-version',
  templateUrl: './create-version.component.html',
  styleUrl: './create-version.component.scss',
})
export class CreateVersionComponent {
  constructor(
    private fileSystemService: OcadFileSystemService,
    private versionProvider: OcadVersionerProvider
  ) {}
  @Input({ required: true })
  public fileName: string = '';

  @Input({ required: true })
  public newVersionName: string = '';
  public showSpinner: boolean = false;

  public form: FormGroup = new FormGroup({
    title: new FormControl('', Validators.min(0)),
    description: new FormControl('', Validators.min(0)),
  });

  public async createNewVersion(): Promise<void> {
    const versionNumber = OcadDirectoryHelper.getVersionNumberFromVersionName(
      this.newVersionName
    );
    if (!versionNumber) {
      console.warn(
        'Can not create version. Invalid versionName: ',
        this.newVersionName
      );
      return;
    }
    const releaseMetaData: OcadVersionListItemDto = {
      versionCreatedAt: new Date(),
      title: this.form.value.title,
      description: this.form.value.description,
      versionNumber,
      numberOfAddedSymbols: 0,
      numberOfDeletedSymbols: 0,
      numberOfEditedSymbols: 0,
    };
    const releaseDirectoryHandle =
      await this.versionProvider.getReleasesDirectoryHandle();
    if (!releaseDirectoryHandle) {
      console.warn('No directory handle for Releases folder');
      return;
    }
    this.showSpinner = true;
    await this.fileSystemService.copyOcdFileToNewReleaseFolder(
      this.newVersionName,
      this.versionProvider.getOcdFileHandle('current'),
      releaseDirectoryHandle
    );
    await this.fileSystemService.setJsonMetaDataFileToReleaseFolder(
      releaseMetaData,
      releaseDirectoryHandle
    );
    this.form.reset();
    await this.versionProvider.updateFileHandleTree();
    this.showSpinner = false;
  }
}
