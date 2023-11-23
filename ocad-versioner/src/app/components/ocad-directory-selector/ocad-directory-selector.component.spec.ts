import { ComponentFixture, TestBed } from '@angular/core/testing';

import { OcadDirectorySelectorComponent } from './ocad-directory-selector.component';

describe('OcadDirectorySelectorComponent', () => {
  let component: OcadDirectorySelectorComponent;
  let fixture: ComponentFixture<OcadDirectorySelectorComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [OcadDirectorySelectorComponent]
    })
    .compileComponents();
    
    fixture = TestBed.createComponent(OcadDirectorySelectorComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
