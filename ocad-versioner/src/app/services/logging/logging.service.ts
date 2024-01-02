import { Injectable, isDevMode } from '@angular/core';
import {
  ApplicationInsights,
  eSeverityLevel,
} from '@microsoft/applicationinsights-web';

@Injectable()
export class LoggingService {
  appInsights: ApplicationInsights;
  constructor() {
    this.appInsights = new ApplicationInsights({
      config: {
        instrumentationKey: '<insert key>',
        enableAutoRouteTracking: true, // option to log all route changes
      },
    });
    this.appInsights.loadAppInsights();
  }

  public logPageView(name?: string, url?: string) {
    if (isDevMode()) return;
    this.appInsights.trackPageView({
      name: name,
      uri: url,
    });
  }

  public logEvent(name: string, properties?: { [key: string]: any }) {
    if (isDevMode()) return;
    this.appInsights.trackEvent({ name: name }, properties);
  }

  public logMetric(
    name: string,
    average: number,
    properties?: { [key: string]: any }
  ) {
    if (isDevMode()) return;
    this.appInsights.trackMetric({ name: name, average: average }, properties);
  }

  public logException(exception: Error, severityLevel?: number) {
    if (isDevMode()) return;
    this.appInsights.trackException({
      exception: exception,
      severityLevel: severityLevel,
    });
  }

  public logInformation(message: string, properties?: { [key: string]: any }) {
    this.appInsights.trackTrace(
      {
        message,
        severityLevel: eSeverityLevel.Information,
      },
      properties
    );
  }

  public logWarning(message: string, properties?: { [key: string]: any }) {
    this.appInsights.trackTrace(
      {
        message,
        severityLevel: eSeverityLevel.Warning,
      },
      properties
    );
  }

  public logError(message: string, properties?: { [key: string]: any }) {
    this.appInsights.trackTrace(
      {
        message,
        severityLevel: eSeverityLevel.Error,
      },
      properties
    );
  }
}
