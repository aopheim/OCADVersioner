import { ComponentFixture, TestBed } from '@angular/core/testing';

import { OcadVersionerComponent } from './ocad-versioner.component';

describe('OcadVersionerComponent', () => {
  let component: OcadVersionerComponent;
  let fixture: ComponentFixture<OcadVersionerComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [OcadVersionerComponent]
    })
    .compileComponents();
    
    fixture = TestBed.createComponent(OcadVersionerComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
