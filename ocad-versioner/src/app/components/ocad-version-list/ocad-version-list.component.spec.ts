import { ComponentFixture, TestBed } from '@angular/core/testing';

import { OcadVersionListComponent } from './ocad-version-list.component';

describe('OcadVersionListComponent', () => {
  let component: OcadVersionListComponent;
  let fixture: ComponentFixture<OcadVersionListComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [OcadVersionListComponent]
    })
    .compileComponents();
    
    fixture = TestBed.createComponent(OcadVersionListComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
