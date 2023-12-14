import { ComponentFixture, TestBed } from '@angular/core/testing';

import { CurrentVersionComparisonComponent } from './current-version-comparison.component';

describe('CurrentVersionComparisonComponent', () => {
  let component: CurrentVersionComparisonComponent;
  let fixture: ComponentFixture<CurrentVersionComparisonComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [CurrentVersionComparisonComponent]
    })
    .compileComponents();
    
    fixture = TestBed.createComponent(CurrentVersionComparisonComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
