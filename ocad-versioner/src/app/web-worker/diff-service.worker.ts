/// <reference lib="webworker" />

import { OcadDiffDto } from '../components/ocad-diff-table/ocad-diff-table/ocad-diff-table.models';
import { JsonDiffService } from '../services/json-diff-service/json-diff-service';
import { JsonDiffServiceInput } from '../services/json-diff-service/json-diff-service.models';

addEventListener('message', ({ data }) => {
  function reportProgress(progress: number): void {
    postMessage(progress);
  }

  const diffDto: JsonDiffServiceInput = data;
  const diff: OcadDiffDto = JsonDiffService.calculateJsonDiff(
    diffDto.oldVersion,
    diffDto.newVersion,
    reportProgress
  );
  postMessage(diff);
});
