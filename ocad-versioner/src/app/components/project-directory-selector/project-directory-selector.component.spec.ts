import { ComponentFixture, TestBed } from '@angular/core/testing';

import { ProjectDirectorySelectorComponent } from './project-directory-selector.component';

describe('OcadFileUploaderComponent', () => {
  let component: ProjectDirectorySelectorComponent;
  let fixture: ComponentFixture<ProjectDirectorySelectorComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({}).compileComponents();

    fixture = TestBed.createComponent(ProjectDirectorySelectorComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
