import { Component } from '@angular/core';
import { OcadVersionListDto as OcadVersionListItemDto } from './ocad-version-list.models';

@Component({
  selector: 'ocad-version-list',
  templateUrl: './ocad-version-list.component.html',
  styleUrl: './ocad-version-list.component.scss',
})
export class OcadVersionListComponent {
  public selectedVersionNumber: number = 1;
  public releases: OcadVersionListItemDto[] = [
    {
      title: 'Dette er versjon 1',
      description: 'Importert alle høydekurver',
      versionCreatedAt: new Date(2023, 4, 15),
      versionNumber: 3,
      numberOfAddedSymbols: 150,
      numberOfDeletedSymbols: 4,
      numberOfEditedSymbols: 2,
    },
    {
      title: 'Importert veidata fra OSM',
      description: 'Må nok ses litt mer over',
      versionCreatedAt: new Date(2023, 4, 18),
      versionNumber: 2,
      numberOfAddedSymbols: 15,
      numberOfDeletedSymbols: 4,
      numberOfEditedSymbols: 2,
    },
    {
      title: 'Skisser fra synfaring 20.april',
      description: 'Fra søndre og sørvestre del',
      versionCreatedAt: new Date(2023, 4, 20),
      versionNumber: 3,
      numberOfAddedSymbols: 160,
      numberOfDeletedSymbols: 2,
      numberOfEditedSymbols: 75,
    },
  ];
}
