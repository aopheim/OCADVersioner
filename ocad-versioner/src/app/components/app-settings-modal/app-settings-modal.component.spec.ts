import { ComponentFixture, TestBed } from '@angular/core/testing';

import { AppSettingsModalComponent } from './app-settings-modal.component';

describe('AppSettingsModalComponent', () => {
  let component: AppSettingsModalComponent;
  let fixture: ComponentFixture<AppSettingsModalComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AppSettingsModalComponent]
    })
    .compileComponents();
    
    fixture = TestBed.createComponent(AppSettingsModalComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
