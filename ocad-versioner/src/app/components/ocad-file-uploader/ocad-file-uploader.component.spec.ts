import { ComponentFixture, TestBed } from '@angular/core/testing';

import { OcadFileUploaderComponent } from './ocad-file-uploader.component';

describe('OcadDirectorySelectorComponent', () => {
  let component: OcadFileUploaderComponent;
  let fixture: ComponentFixture<OcadFileUploaderComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [OcadFileUploaderComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(OcadFileUploaderComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
