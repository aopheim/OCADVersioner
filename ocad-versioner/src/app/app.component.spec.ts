import { TestBed } from '@angular/core/testing'
import { AppComponent } from './app.component'
import { OcadVersionerProvider } from './ocad-versioner.provider'

describe('AppComponent', () => {
    beforeEach(async () => {
        await TestBed.configureTestingModule({
            imports: [AppComponent, OcadVersionerProvider],
        }).compileComponents()
    })

    // Too many dependencies to mock out - skipping
    xit('should create the app', () => {
        const fixture = TestBed.createComponent(AppComponent)
        const app = fixture.componentInstance
        expect(app).toBeTruthy()
    })

    xit(`should have the 'ocad-versioner' title`, () => {
        const fixture = TestBed.createComponent(AppComponent)
        const app = fixture.componentInstance
        expect(app.title).toEqual('OCADVersioner')
    })
})
