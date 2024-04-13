import { ErrorHandler, Injectable } from '@angular/core';
import { LoggingService } from '../logging/logging.service';

@Injectable()
export class ErrorHandlerService extends ErrorHandler {
  constructor(private loggingService: LoggingService) {
    super();
  }

  public override handleError(error: Error) {
    console.log('ErrorHandlerService received ', error);
    this.loggingService.logException(error);
  }
}
