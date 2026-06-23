import { Component, ElementRef, OnInit, ViewChild, AfterViewInit } from '@angular/core';
import * as THREE from 'three';
import { CargoBox, LogisticsService } from './logistics.service';


@Component({
  selector: 'app-root',
  template: `
    <div class="dashboard">
      <div class="sidebar">
        <h2>Dispatch Engine</h2>
        <button (click)="runAlgorithm(1)">Calculate Route 1 Load</button>
      </div>
      <div class="canvas-container">
        <div #rendererContainer class="render-target"></div>
      </div>
    </div>
  `,
  styles: [`
    .dashboard { display: flex; height: 100vh; font-family: sans-serif; }
    .sidebar { width: 250px; padding: 20px; background: #f4f4f4; border-right: 1px solid #ccc; }
    .canvas-container { flex: 1; position: relative; background: #222; }
    .render-target { width: 100%; height: 100%; }
    button { padding: 10px 15px; cursor: pointer; background: #007bff; color: white; border: none; }
  `]
})
export class AppComponent implements OnInit, AfterViewInit {
  @ViewChild('rendererContainer', { static: true }) rendererContainer!: ElementRef;
  
  private scene!: THREE.Scene;
  private camera!: THREE.PerspectiveCamera;
  private renderer!: THREE.WebGLRenderer;
  private truckGroup = new THREE.Group();

  // Standard 53ft Trailer dimensions in mm (matches your SQL Insert script)
  private TRUCK_L = 5300;
  private TRUCK_W = 2400;
  private TRUCK_H = 2600;

  constructor(private logisticsService: LogisticsService) {}

  ngOnInit(): void {}

  ngAfterViewInit(): void {
    this.init3DScene();
  }

  private init3DScene() {
    const container = this.rendererContainer.nativeElement;

    // 1. Scene Setup
    this.scene = new THREE.Scene();
    
    // 2. Camera Setup
    this.camera = new THREE.PerspectiveCamera(45, container.clientWidth / container.clientHeight, 1, 20000);
    // Position camera diagonally looking down at the truck
    this.camera.position.set(this.TRUCK_W * 1.5, this.TRUCK_H * 2, this.TRUCK_L * 1.5);
    this.camera.lookAt(this.TRUCK_W / 2, this.TRUCK_H / 2, this.TRUCK_L / 2);

    // 3. Renderer Setup
    this.renderer = new THREE.WebGLRenderer({ antialias: true, alpha: true });
    this.renderer.setSize(container.clientWidth, container.clientHeight);
    container.appendChild(this.renderer.domElement);

    // 4. Lighting
    const ambientLight = new THREE.AmbientLight(0xffffff, 0.6);
    this.scene.add(ambientLight);
    const dirLight = new THREE.DirectionalLight(0xffffff, 0.8);
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
    // Translate geometry so the bottom-left-back is at (0,0,0)
    geometry.translate(this.TRUCK_W / 2, this.TRUCK_H / 2, this.TRUCK_L / 2);
    
    const edges = new THREE.EdgesGeometry(geometry);
    const material = new THREE.LineBasicMaterial({ color: 0x00ff00, linewidth: 2 });
    const truckWireframe = new THREE.LineSegments(edges, material);
    
    this.truckGroup.add(truckWireframe);
  }

  public runAlgorithm(routeId: number) {
    this.logisticsService.calculatePacking(routeId).subscribe({
      next: (res) => {
        console.log(res.message);
        this.renderBoxes(res.packedBoxes);
      },
      error: (err) => console.error('Failed to run packing algorithm', err)
    });
  }

  private renderBoxes(boxes: CargoBox[]) {
    // Clear previously packed boxes
    const toRemove = this.truckGroup.children.filter(c => c.type === 'Mesh');
    toRemove.forEach(mesh => this.truckGroup.remove(mesh));

    boxes.forEach(box => {
      // Color code based on Fragility (Red = Fragile, Blue = Sturdy)
      const color = box.isFragile ? 0xff4444 : 0x4444ff;
      
      const geometry = new THREE.BoxGeometry(box.width, box.height, box.length);
      const material = new THREE.MeshLambertMaterial({ color: color, transparent: true, opacity: 0.8 });
      const mesh = new THREE.Mesh(geometry, material);

      // THREE.js BoxGeometry sets the origin at the center. 
      // We must offset the API coordinates (which are bottom-left) by half the dimensions.
      const centerX = box.packedX + (box.width / 2);
      const centerY = box.packedZ + (box.height / 2); // Z from API is Height (Y) in 3D space
      const centerZ = box.packedY + (box.length / 2); // Y from API is Depth (Z) in 3D space

      mesh.position.set(centerX, centerY, centerZ);
      
      // Add a subtle black edge to make overlapping boxes visible
      const edges = new THREE.EdgesGeometry(geometry);
      const edgeLine = new THREE.LineSegments(edges, new THREE.LineBasicMaterial({ color: 0x000000 }));
      mesh.add(edgeLine);

      this.truckGroup.add(mesh);
    });
  }
}