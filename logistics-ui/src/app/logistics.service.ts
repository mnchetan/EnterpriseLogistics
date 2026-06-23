import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface CargoBox {
  boxId: number;
  stopSequence: number;
  length: number;
  width: number;
  height: number;
  weight: number;
  isFragile: boolean;
  
  // Add this line to fix the TS2551 error!
  isPacked: boolean; 
  
  packedX: number;
  packedY: number;
  packedZ: number;
}

export interface PackResponse {
  message: string;
  packedBoxes: CargoBox[];
}

@Injectable({
  providedIn: 'root'
})
export class LogisticsService {
  // Update this to match your .NET launchSettings.json port
  private apiUrl = 'http://localhost:5059/Algorithm';

  constructor(private http: HttpClient) { }

  public calculatePacking(routeId: number): Observable<PackResponse> {
    return this.http.post<PackResponse>(`${this.apiUrl}/pack/${routeId}`, {});
  }

  // 1. Fetch the current state of the database without running the algorithm
  public loadExistingPlan(routeId: number): Observable<any[]> {
    return this.http.get<any[]>(`http://localhost:5059/tinyWebApi/Get/GetCargoForRoute/DataTableText?RouteId=${routeId}`);
  }

  // 2. Save a specific box's coordinates after drag-and-drop
  public updateBoxCoordinates(box: CargoBox): Observable<any> {
    const payload = {
      BoxId: box.boxId,
      PackedX: box.packedX,
      PackedY: box.packedY,
      PackedZ: box.packedZ
    };
    return this.http.post(`http://localhost:5059/tinyWebApi/Post/UpdateBoxCoordinates/NonQueryText`, payload);
  }
}