import { Injectable, isDevMode } from '@angular/core';
import {
  ApplicationInsights,
  eSeverityLevel,
} from '@microsoft/applicationinsights-web';
import { isNil } from 'lodash-es';
import { environment } from '../../../environments/environment.prod';

@Injectable()
export class LoggingService {
  private appInsights: ApplicationInsights | null = null;
  constructor() {
    if (isDevMode()) {
      console.warn('App is running locally. Not setting up App Insights.');
      this.appInsights = null;
      return;
    }
    if (isNil(environment.APPINSIGHTSINSTRUMENTATIONKEY)) {
      console.warn('Missing config to setup App Insights');
      console.log('environment: ', environment);
      return;
    }
    const instrumentationKey = environment.APPINSIGHTSINSTRUMENTATIONKEY;
    this.appInsights = new ApplicationInsights({
      config: {
        instrumentationKey,
        enableAutoRouteTracking: true,
      },
    });
    this.appInsights.loadAppInsights();
    console.log('App Insights set up');
  }

  public logPageView(name?: string, url?: string) {
    if (isDevMode()) return;
    this.appInsights?.trackPageView({
      name: name,
      uri: url,
    });
  }

  public logEvent(name: string, properties?: { [key: string]: any }) {
    if (isDevMode()) return;
    this.appInsights?.trackEvent({ name: name }, properties);
  }

  public logMetric(
    name: string,
    average: number,
    properties?: { [key: string]: any }
  ) {
    if (isDevMode()) return;
    this.appInsights?.trackMetric({ name: name, average: average }, properties);
  }

  public logException(
    exception: Error,
    severityLevel?: number,
    logToConsole: boolean = true
  ) {
    this.appInsights?.trackException({
      exception: exception,
      severityLevel: severityLevel,
    });
    if (logToConsole) console.error(exception);
  }

  public logInformation(
    message: string,
    properties?: { [key: string]: any },
    logToConsole: boolean = false
  ) {
    this.appInsights?.trackTrace(
      {
        message,
        severityLevel: eSeverityLevel.Information,
      },
      properties
    );
    if (logToConsole) console.log(message);
  }

  public logWarning(
    message: string,
    properties?: { [key: string]: any },
    logToConsole: boolean = false
  ) {
    this.appInsights?.trackTrace(
      {
        message,
        severityLevel: eSeverityLevel.Warning,
      },
      properties
    );
    if (logToConsole) console.warn(message);
  }

  public logError(
    message: string,
    properties?: { [key: string]: any },
    logToConsole: boolean = false
  ) {
    this.appInsights?.trackTrace(
      {
        message,
        severityLevel: eSeverityLevel.Error,
      },
      properties
    );
    if (logToConsole) console.error(message);
  }
}
