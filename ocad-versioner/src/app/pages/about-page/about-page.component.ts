import { Component } from '@angular/core'
import { Router } from '@angular/router'
import { TranslateService } from '@ngx-translate/core'

@Component({
    selector: 'about-page',
    templateUrl: './about-page.component.html',
    styleUrl: './about-page.component.scss',
})
export class AboutPageComponent {
    constructor(
        private router: Router,
        private translateService: TranslateService
    ) {}

    public selectLanguage(countryCode: string): void {
        this.translateService.use(countryCode)
    }

    public navigateToMainPage(): void {
        this.router.navigate(['./'])
    }
}
