import { ComponentFixture, TestBed } from '@angular/core/testing';

import { OcadMapViewerComponent } from './ocad-map-viewer.component';

describe('OcadMapViewerComponent', () => {
  let component: OcadMapViewerComponent;
  let fixture: ComponentFixture<OcadMapViewerComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({}).compileComponents();

    fixture = TestBed.createComponent(OcadMapViewerComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
