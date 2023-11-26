import { ComponentFixture, TestBed } from '@angular/core/testing';

import { OcadDiffTableComponent } from './ocad-diff-table.component';

describe('OcadDiffTableComponent', () => {
  let component: OcadDiffTableComponent;
  let fixture: ComponentFixture<OcadDiffTableComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      declarations: [ OcadDiffTableComponent ]
    })
    .compileComponents();
  });

  beforeEach(() => {
    fixture = TestBed.createComponent(OcadDiffTableComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
