import { Injectable } from '@angular/core';
import { IJsonDiffService } from './json-diff-service.models';
import {
  AddedSymbolDto,
  DeletedSymbolDto,
  EditedSymbolDto,
  OcadDiffDto,
} from '../components/ocad-diff-table/ocad-diff-table.models';
import { isNil } from 'lodash-es';
import {
  Feature,
  FeatureCollection,
  GeoJsonProperties,
  Geometry,
} from 'geojson';

@Injectable()
export class JsonDiffService implements IJsonDiffService {
  getJsonDiff(
    oldFeatures: FeatureCollection,
    newFeatures: FeatureCollection
  ): OcadDiffDto {
    const added: AddedSymbolDto[] = [];
    const edited: EditedSymbolDto[] = [];
    const deleted: DeletedSymbolDto[] = [];
    newFeatures.features.forEach((newFeature) => {
      const existsInOld = oldFeatures.features.find(
        (f) => f.id === newFeature.id
      );
      if (isNil(existsInOld)) {
        const symbolName: string = this.getSymbolName(newFeature);

        const toAdd: AddedSymbolDto = {
          createdAtUtc: new Date(),
          symbolName: symbolName,
          symbolNumber: symbolName,
          lastEditBy: '',
        };
        added.push(toAdd);
      } else {
        const editedItem: EditedSymbolDto = {
          symbolName: this.getSymbolName(newFeature),
          symbolNumber: this.getSymbolName(newFeature),
          createdAtUtc: new Date(),
        };
        edited.push(editedItem);
      }
    });

    return {
      added,
      deleted,
      edited: [],
    } as OcadDiffDto;
  }

  private getSymbolName(
    newFeature: Feature<Geometry, GeoJsonProperties>
  ): string {
    return (newFeature.properties?.['sym'] ?? '') as string;
  }
}
