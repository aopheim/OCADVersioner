import { ComponentFixture, TestBed } from '@angular/core/testing';

import { DiffTableTabsComponent } from './diff-table-tabs.component';

describe('DiffTableTabsComponent', () => {
  let component: DiffTableTabsComponent;
  let fixture: ComponentFixture<DiffTableTabsComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [DiffTableTabsComponent]
    })
    .compileComponents();
    
    fixture = TestBed.createComponent(DiffTableTabsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
