import { ComponentFixture, TestBed } from '@angular/core/testing';

import { OcadVersionerComponent } from './ocad-versioner.component';
import { JsonDiffService } from './services/json-diff-service/json-diff-service';
import { OcadVersionerComponentsModule as OcadVersionerComponentsModule } from './components/ocad-versioner-components.module';
import { TranslateModule, TranslateService } from '@ngx-translate/core';

describe('OcadVersionerComponent', () => {
  let component: OcadVersionerComponent;
  let fixture: ComponentFixture<OcadVersionerComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [TranslateModule.forRoot()],
      providers: [JsonDiffService],
    }).compileComponents();

    fixture = TestBed.createComponent(OcadVersionerComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  xit('should create', () => {
    expect(component).toBeTruthy();
  });
});
