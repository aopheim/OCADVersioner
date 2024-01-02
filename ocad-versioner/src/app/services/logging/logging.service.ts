import { Injectable, isDevMode } from '@angular/core';
import { AppConfigurationClient } from '@azure/app-configuration';
import {
  ApplicationInsights,
  eSeverityLevel,
} from '@microsoft/applicationinsights-web';
import { environment } from '../../../environments/environment.prod';
import { GlobalConstants } from '../../app.constants';

@Injectable()
export class LoggingService {
  private appInsights: ApplicationInsights | null = null;
  constructor() {
    if (isDevMode()) {
      this.appInsights = null;
      return;
    }
    const appConfigClient = new AppConfigurationClient(
      environment.AZURE_APP_CONFIG_CONNECTION_STRING
    );
    this.getSecret(
      appConfigClient,
      GlobalConstants.AppInsightsInstrumentationKey
    ).then((instrumentationKey) => {
      this.appInsights = new ApplicationInsights({
        config: {
          instrumentationKey,
          enableAutoRouteTracking: true,
        },
      });
      this.appInsights.loadAppInsights();
    });
  }

  private async getSecret(
    client: AppConfigurationClient,
    key: string
  ): Promise<string | undefined> {
    const setting = await client.getConfigurationSetting({
      key,
    });

    return setting.value;
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

  public logException(exception: Error, severityLevel?: number) {
    if (isDevMode()) return;
    this.appInsights?.trackException({
      exception: exception,
      severityLevel: severityLevel,
    });
  }

  public logInformation(message: string, properties?: { [key: string]: any }) {
    this.appInsights?.trackTrace(
      {
        message,
        severityLevel: eSeverityLevel.Information,
      },
      properties
    );
  }

  public logWarning(message: string, properties?: { [key: string]: any }) {
    this.appInsights?.trackTrace(
      {
        message,
        severityLevel: eSeverityLevel.Warning,
      },
      properties
    );
  }

  public logError(message: string, properties?: { [key: string]: any }) {
    this.appInsights?.trackTrace(
      {
        message,
        severityLevel: eSeverityLevel.Error,
      },
      properties
    );
  }
}
