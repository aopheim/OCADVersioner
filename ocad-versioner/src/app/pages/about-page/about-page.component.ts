import { Component, OnDestroy } from '@angular/core'
import { takeUntilDestroyed } from '@angular/core/rxjs-interop'
import { Title } from '@angular/platform-browser'
import { Router } from '@angular/router'
import { TranslateService } from '@ngx-translate/core'

@Component({
    selector: 'about-page',
    templateUrl: './about-page.component.html',
    styleUrl: './about-page.component.scss',
})
export class AboutPageComponent implements OnDestroy {
    // The title this page replaces, put back when the page goes away.
    private readonly previousTitle: string

    constructor(
        private router: Router,
        private translateService: TranslateService,
        private title: Title
    ) {
        // A <title> element in the template has no effect in the body, so the
        // tab title is set here and follows language changes.
        this.previousTitle = this.title.getTitle()
        this.translateService
            .stream(['#_OCADVersioner', '#_about'])
            .pipe(takeUntilDestroyed())
            .subscribe((t) =>
                this.title.setTitle(`${t['#_OCADVersioner']} - ${t['#_about']}`)
            )
    }

    public ngOnDestroy(): void {
        this.title.setTitle(this.previousTitle)
    }

    public selectLanguage(countryCode: string): void {
        this.translateService.use(countryCode)
    }

    public navigateToMainPage(): void {
        this.router.navigate(['./'])
    }
}
