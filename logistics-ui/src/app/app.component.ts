import { Component, ElementRef, OnInit, ViewChild, AfterViewInit } from '@angular/core';
import * as THREE from 'three';
import { LogisticsService, CargoBox } from './logistics.service';

@Component({
  selector: 'app-root',
  template: `
    <div class="dashboard-container">
      
      <div class="header">
        <h2>Enterprise Dispatch Engine</h2>
        <button (click)="runAlgorithm(1)" class="btn-primary">Calculate Route 1 Load Plan</button>
      </div>

      <div class="content-split">
        
        <div class="canvas-panel">
          <div class="panel-title">3D Volumetric View (Guillotine Split)</div>
          <div #rendererContainer class="render-target"></div>
          
          <div class="legend">
            <span class="legend-item"><div class="color-box blue"></div> Sturdy Base</span>
            <span class="legend-item"><div class="color-box red"></div> Fragile Top</span>
          </div>
        </div>

        <div class="data-panel">
          <div class="panel-title">Manifest & Placement Audit</div>
          
          <div class="table-container">
            <table class="data-table">
              <thead>
                <tr>
                  <th>Box ID</th>
                  <th>Stop</th>
                  <th>Type</th>
                  <th>Dims (W x L x H)</th>
                  <th>Weight</th>
                  <th>Pos (X, Y, Z)</th>
                </tr>
              </thead>
              <tbody>
                <tr *ngIf="packedBoxes.length === 0">
                  <td colspan="6" class="empty-state">No load plan calculated yet.</td>
                </tr>
                
                <tr *ngFor="let box of packedBoxes" 
                    [ngClass]="{'fragile-row': box.isFragile, 'sturdy-row': !box.isFragile}">
                  <td>#{{ box.boxId }}</td>
                  <td>Stop {{ box.stopSequence }}</td>
                  <td>
                    <span class="badge" [ngClass]="box.isFragile ? 'badge-red' : 'badge-blue'">
                      {{ box.isFragile ? 'Fragile' : 'Sturdy' }}
                    </span>
                  </td>
                  <td>{{ box.width }} x {{ box.length }} x {{ box.height }}</td>
                  <td>{{ box.weight }} kg</td>
                  <td class="coords">({{ box.packedX }}, {{ box.packedY }}, {{ box.packedZ }})</td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>

      </div>
    </div>
  `,
  styles: [`
    /* Core Layout */
    .dashboard-container { display: flex; flex-direction: column; height: 100vh; font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; background: #1e1e1e; color: #fff; }
    .header { padding: 15px 25px; background: #2d2d30; display: flex; justify-content: space-between; align-items: center; border-bottom: 1px solid #3e3e42; }
    .header h2 { margin: 0; font-size: 1.2rem; font-weight: 400; letter-spacing: 1px; color: #007acc; }
    .btn-primary { background: #007acc; color: white; border: none; padding: 10px 20px; font-weight: bold; cursor: pointer; border-radius: 4px; transition: 0.2s; }
    .btn-primary:hover { background: #005f9e; }

    /* Split View */
    .content-split { display: flex; flex: 1; overflow: hidden; }
    .canvas-panel { flex: 3; display: flex; flex-direction: column; border-right: 1px solid #3e3e42; position: relative; }
    .data-panel { flex: 2; display: flex; flex-direction: column; background: #252526; }
    .panel-title { padding: 10px 15px; background: #333337; font-size: 0.9rem; font-weight: 600; text-transform: uppercase; letter-spacing: 0.5px; border-bottom: 1px solid #1e1e1e; }
    
    /* 3D Canvas */
    .render-target { flex: 1; width: 100%; height: 100%; }
    .legend { position: absolute; bottom: 20px; left: 20px; background: rgba(0,0,0,0.7); padding: 10px; border-radius: 4px; display: flex; gap: 15px; font-size: 0.85rem; }
    .legend-item { display: flex; align-items: center; gap: 8px; }
    .color-box { width: 15px; height: 15px; border-radius: 3px; }
    .color-box.blue { background: #4444ff; }
    .color-box.red { background: #ff4444; }

    /* Data Table */
    .table-container { flex: 1; overflow-y: auto; padding: 10px; }
    .data-table { width: 100%; border-collapse: collapse; font-size: 0.9rem; text-align: left; }
    .data-table th { position: sticky; top: 0; background: #333337; padding: 12px 10px; color: #cccccc; font-weight: 500; border-bottom: 2px solid #007acc; }
    .data-table td { padding: 10px; border-bottom: 1px solid #3e3e42; }
    .empty-state { text-align: center; color: #888; padding: 30px !important; font-style: italic; }
    
    /* Table Rows & Badges */
    .sturdy-row:hover { background: rgba(68, 68, 255, 0.1); }
    .fragile-row:hover { background: rgba(255, 68, 68, 0.1); }
    .coords { font-family: monospace; color: #4ec9b0; }
    .badge { padding: 3px 8px; border-radius: 12px; font-size: 0.75rem; font-weight: bold; text-transform: uppercase; }
    .badge-blue { background: rgba(68, 68, 255, 0.2); color: #8888ff; border: 1px solid #4444ff; }
    .badge-red { background: rgba(255, 68, 68, 0.2); color: #ff8888; border: 1px solid #ff4444; }
  `]
})
export class AppComponent implements OnInit, AfterViewInit {
  @ViewChild('rendererContainer', { static: true }) rendererContainer!: ElementRef;
  
  // Expose the packed boxes to the HTML template
  public packedBoxes: CargoBox[] = [];

  private scene!: THREE.Scene;
  private camera!: THREE.PerspectiveCamera;
  private renderer!: THREE.WebGLRenderer;
  private truckGroup = new THREE.Group();

  // Standard 53ft Trailer dimensions in mm
  private TRUCK_L = 5300;
  private TRUCK_W = 2400;
  private TRUCK_H = 2600;

  constructor(private logisticsService: LogisticsService) {}

  ngOnInit(): void {}

  ngAfterViewInit(): void {
    this.init3DScene();
    
    // Add a window resize listener so the 3D canvas scales perfectly
    window.addEventListener('resize', () => {
      const container = this.rendererContainer.nativeElement;
      this.camera.aspect = container.clientWidth / container.clientHeight;
      this.camera.updateProjectionMatrix();
      this.renderer.setSize(container.clientWidth, container.clientHeight);
    });
  }

  private init3DScene() {
    const container = this.rendererContainer.nativeElement;

    // 1. Scene Setup
    this.scene = new THREE.Scene();
    // Match the dark theme background
    this.scene.background = new THREE.Color('#1e1e1e');
    
    // 2. Camera Setup
    this.camera = new THREE.PerspectiveCamera(45, container.clientWidth / container.clientHeight, 1, 20000);
    this.camera.position.set(this.TRUCK_W * 1.8, this.TRUCK_H * 1.5, this.TRUCK_L * 1.5);
    this.camera.lookAt(this.TRUCK_W / 2, this.TRUCK_H / 2, this.TRUCK_L / 2);

    // 3. Renderer Setup
    this.renderer = new THREE.WebGLRenderer({ antialias: true });
    this.renderer.setSize(container.clientWidth, container.clientHeight);
    container.appendChild(this.renderer.domElement);

    // 4. Lighting
    const ambientLight = new THREE.AmbientLight(0xffffff, 0.7);
    this.scene.add(ambientLight);
    const dirLight = new THREE.DirectionalLight(0xffffff, 0.6);
    dirLight.position.set(5000, 5000, 5000);
    this.scene.add(dirLight);

    // 5. Draw the empty truck wireframe
    this.drawTruckWireframe();
    this.scene.add(this.truckGroup);

    // 6. Animation Loop
    const animate = () => {
      requestAnimationFrame(animate);
      this.renderer.render(this.scene, this.camera);
    };
    animate();
  }

  private drawTruckWireframe() {
    const geometry = new THREE.BoxGeometry(this.TRUCK_W, this.TRUCK_H, this.TRUCK_L);
    geometry.translate(this.TRUCK_W / 2, this.TRUCK_H / 2, this.TRUCK_L / 2);
    
    const edges = new THREE.EdgesGeometry(geometry);
    const material = new THREE.LineBasicMaterial({ color: 0x00ff00, linewidth: 2, opacity: 0.3, transparent: true });
    const truckWireframe = new THREE.LineSegments(edges, material);
    
    this.truckGroup.add(truckWireframe);
  }

  public runAlgorithm(routeId: number) {
    this.logisticsService.calculatePacking(routeId).subscribe({
      next: (res) => {
        console.log(res.message);
        // Save the boxes so the Angular HTML table can render them
        this.packedBoxes = res.packedBoxes;
        // Render the 3D meshes
        this.renderBoxes(res.packedBoxes);
      },
      error: (err) => console.error('Failed to run packing algorithm', err)
    });
  }

  private renderBoxes(boxes: CargoBox[]) {
    // Clear previously packed boxes
    const toRemove = this.truckGroup.children.filter(c => c.type === 'Mesh' || c.type === 'Group');
    toRemove.forEach(mesh => this.truckGroup.remove(mesh));

    boxes.forEach(box => {
      // Skip if algorithm determined it couldn't fit
      if (!box.isPacked) return;

      const color = box.isFragile ? 0xff4444 : 0x4444ff;
      
      const geometry = new THREE.BoxGeometry(box.width, box.height, box.length);
      const material = new THREE.MeshLambertMaterial({ color: color, transparent: true, opacity: 0.85 });
      const mesh = new THREE.Mesh(geometry, material);

      // THREE.js Offset Logic
      const centerX = box.packedX + (box.width / 2);
      const centerY = box.packedZ + (box.height / 2); 
      const centerZ = box.packedY + (box.length / 2); 

      mesh.position.set(centerX, centerY, centerZ);
      
      // Add edges
      const edges = new THREE.EdgesGeometry(geometry);
      const edgeLine = new THREE.LineSegments(edges, new THREE.LineBasicMaterial({ color: 0xffffff, transparent: true, opacity: 0.3 }));
      mesh.add(edgeLine);

      this.truckGroup.add(mesh);
    });
  }
}