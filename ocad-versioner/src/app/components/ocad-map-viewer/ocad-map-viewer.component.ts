import { Component } from '@angular/core';

@Component({
  selector: 'ocad-map-viewer',
  templateUrl: './ocad-map-viewer.component.html',
  styleUrl: './ocad-map-viewer.component.scss',
})
export class OcadMapViewerComponent  {
  public options: any;

  // ngOnInit(): void {
  //   this.options = {
  //     layers: [
  //       tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
  //         maxZoom: 18,
  //         attribution: '...',
  //       }),
  //     ],
  //     zoom: 5,
  //     center: latLng(46.879966, -121.726909),
  //   };
  // }
}
