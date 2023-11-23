import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import * as ocad2geojson from 'ocad2geojson';

@Component({
  selector: 'ocad-directory-selector',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './ocad-directory-selector.component.html',
  styleUrl: './ocad-directory-selector.component.scss',
})
export class OcadDirectorySelectorComponent {
  constructor() {
    ocad2geojson.readOcad('C:\\repos\\ocad-storigarden\\storigarden.ocd');
  }
}
