import { ComponentFixture, TestBed } from '@angular/core/testing'

import { OcadDiffTableComponent } from './ocad-diff-table.component'
import { TranslateModule, TranslateService } from '@ngx-translate/core'

describe('OcadDiffTableComponent', () => {
    let component: OcadDiffTableComponent
    let fixture: ComponentFixture<OcadDiffTableComponent>

    beforeEach(async () => {
        await TestBed.configureTestingModule({
            imports: [TranslateModule.forRoot()],
            declarations: [OcadDiffTableComponent],
        }).compileComponents()
    })

    beforeEach(() => {
        fixture = TestBed.createComponent(OcadDiffTableComponent)
        component = fixture.componentInstance
        fixture.detectChanges()
    })

    it('should create', () => {
        expect(component).toBeTruthy()
    })
})
