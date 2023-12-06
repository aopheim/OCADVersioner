import { Component, Input } from '@angular/core';
import { FormControl, FormGroup, Validators } from '@angular/forms';
import { OcadFileSystemService } from '../../services/ocad-file-system-service';
import { OcadVersionerProvider } from '../../ocad-versioner.provider';

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
    // const releaseMetaData: OcadVersionMetaData = {
    //   versionCreatedAt: new Date(),
    //   title: this.form.value.title,
    //   description: this.form.value.description,
    //   numberOfAddedSymbols: 0,
    //   numberOfDeletedSymbols: 0,
    //   numberOfEditedSymbols: 0,
    // };
    const releaseDirectoryHandle =
      await this.versionProvider.getReleasesDirectoryHandle();
    if (!releaseDirectoryHandle) {
      console.warn('No directory handle for Releases folder');
      return;
    }
    this.showSpinner = true;
    await this.fileSystemService.copyFileToNewReleaseFolder(
      this.newVersionName,
      this.versionProvider.getOcdFileHandle('current'),
      releaseDirectoryHandle
    );
    // const _jsonFile = JSON.stringify(releaseMetaData);
    this.form.reset();
    await this.versionProvider.updateFileHandleTree();
    this.showSpinner = false;
  }
}
