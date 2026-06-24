import { Component, ElementRef, OnInit, ViewChild, AfterViewInit, ChangeDetectorRef } from '@angular/core';
import * as THREE from 'three';
import { OrbitControls } from 'three/examples/jsm/controls/OrbitControls';
import { DragControls } from 'three/examples/jsm/controls/DragControls';
import { LogisticsService, CargoBox } from './logistics.service';

// 1. We isolate the 3D state so each truck has its own independent universe
export interface TruckViewport {
  id: string;
  remainingCapacity: number;
  packedBoxes: CargoBox[];
  scene: THREE.Scene;
  camera: THREE.PerspectiveCamera;
  renderer: THREE.WebGLRenderer;
  truckGroup: THREE.Group;
  cargoGroup: THREE.Group; // <--- NEW
  orbitControls: OrbitControls;
  dragControls?: DragControls;
  draggableMeshes: THREE.Mesh[];
}

@Component({
  selector: 'app-root',
  templateUrl: './app.component.html',
  styleUrls: ['./app.component.css']
})
export class AppComponent implements OnInit, AfterViewInit {
  @ViewChild('rendererContainerA') containerA!: ElementRef;
  @ViewChild('rendererContainerB') containerB!: ElementRef;

  // 2. Initialize the state for both trucks
  public truckA = this.createEmptyViewport('TruckA');
  public truckB = this.createEmptyViewport('TruckB');
  public unassignedBoxes: CargoBox[] = [];

  private TRUCK_L = 5300;
  private TRUCK_W = 2400;
  private TRUCK_H = 2600;

  constructor(
    private logisticsService: LogisticsService,
    private cdr: ChangeDetectorRef
  ) { }

  ngOnInit(): void { }

  ngAfterViewInit() {
    // 3. Boot up both 3D environments, passing the specific container and state object
    this.init3DScene(this.containerA, this.truckA);
    this.init3DScene(this.containerB, this.truckB);

    // 4. Properly scoped resize listener for both canvases
    window.addEventListener('resize', () => {
      this.resizeCanvas(this.containerA, this.truckA);
      this.resizeCanvas(this.containerB, this.truckB);
    });
  }

  private createEmptyViewport(identifier: string): TruckViewport {
    return {
      id: identifier,
      remainingCapacity: 2000,
      packedBoxes: [],
      scene: new THREE.Scene(),
      camera: new THREE.PerspectiveCamera(),
      renderer: new THREE.WebGLRenderer({ antialias: true }),
      truckGroup: new THREE.Group(),
      cargoGroup: new THREE.Group(),
      orbitControls: null as any,
      draggableMeshes: []
    };
  }

  private resizeCanvas(containerRef: ElementRef, state: TruckViewport) {
    const container = containerRef.nativeElement;
    state.camera.aspect = container.clientWidth / container.clientHeight;
    state.camera.updateProjectionMatrix();
    state.renderer.setSize(container.clientWidth, container.clientHeight);
  }

  private init3DScene(containerRef: ElementRef, state: TruckViewport) {
    const container = containerRef.nativeElement; // Correctly use the passed reference

    state.scene.background = new THREE.Color('#1e1e1e');

    state.camera = new THREE.PerspectiveCamera(45, container.clientWidth / container.clientHeight, 1, 20000);
    state.camera.position.set(this.TRUCK_W * 1.8, this.TRUCK_H * 1.5, this.TRUCK_L * 1.5);
    state.camera.lookAt(this.TRUCK_W / 2, this.TRUCK_H / 2, this.TRUCK_L / 2);

    state.renderer.setSize(container.clientWidth, container.clientHeight);
    container.appendChild(state.renderer.domElement);

    state.orbitControls = new OrbitControls(state.camera, state.renderer.domElement);
    state.orbitControls.target.set(this.TRUCK_W / 2, this.TRUCK_H / 2, this.TRUCK_L / 2);
    state.orbitControls.update();

    const ambientLight = new THREE.AmbientLight(0xffffff, 0.7);
    state.scene.add(ambientLight);

    const dirLight = new THREE.DirectionalLight(0xffffff, 0.6);
    dirLight.position.set(5000, 5000, 5000);
    state.scene.add(dirLight);

    this.drawTruckWireframe(state.truckGroup);
    state.scene.add(state.truckGroup);
    state.scene.add(state.cargoGroup); // <--- NEW: Add the cargo group to the scene

    const animate = () => {
      requestAnimationFrame(animate);
      state.orbitControls.update();
      state.renderer.render(state.scene, state.camera);
    };
    animate();
  }

  public runAlgorithm() {
    this.logisticsService.dispatchFleet().subscribe({
      next: (res: any) => {
        // Safe mapper to handle C# PascalCase to JS camelCase
        const mapBoxes = (boxes: any[]) => boxes.map(b => ({
          boxId: b.boxId ?? b.BoxId,
          stopSequence: b.stopSequence ?? b.StopSequence,
          length: b.length ?? b.Length,
          width: b.width ?? b.Width,
          height: b.height ?? b.Height,
          weight: b.weight ?? b.Weight,
          isFragile: b.isFragile ?? b.IsFragile ?? false,
          isPacked: b.isPacked ?? b.IsPacked ?? false,
          packedX: b.packedX ?? b.PackedX ?? 0,
          packedY: b.packedY ?? b.PackedY ?? 0,
          packedZ: b.packedZ ?? b.PackedZ ?? 0
        }));

        // Process Truck A
        const truckAData = res.fleetStatus.find((t: any) => t.id === 'TruckA' || t.Id === 'TruckA');
        if (truckAData) {
          this.truckA.packedBoxes = mapBoxes(truckAData.boxes || truckAData.Boxes);
          this.truckA.remainingCapacity = 2000 - (truckAData.currentLoad || truckAData.CurrentLoad);
          this.renderBoxes(this.truckA);
        }

        // Process Truck B
        const truckBData = res.fleetStatus.find((t: any) => t.id === 'TruckB' || t.Id === 'TruckB');
        if (truckBData) {
          this.truckB.packedBoxes = mapBoxes(truckBData.boxes || truckBData.Boxes);
          this.truckB.remainingCapacity = 2000 - (truckBData.currentLoad || truckBData.CurrentLoad);
          this.renderBoxes(this.truckB);
        }

        // Process Unassigned/Rejected Boxes
        if (res.unassignedBoxes) {
          this.unassignedBoxes = mapBoxes(res.unassignedBoxes);
        }

        // FORCE Angular to update the HTML Grid
        this.cdr.detectChanges(); 
      },
      error: (err: any) => console.error('Failed to dispatch fleet algorithm', err)
    });
  }

  get packedBoxes(): CargoBox[] {
    return [...this.truckA.packedBoxes, ...this.truckB.packedBoxes, ...this.unassignedBoxes];
  }

  private renderBoxes(state: TruckViewport) {
    // 1. Clear existing cargo without destroying the TruckWireframe or Floor
    state.cargoGroup.clear();
    
    // 2. Dispose of old controls to prevent memory leaks/zombie events
    if (state.dragControls) {
      state.dragControls.dispose();
    }
    state.draggableMeshes = [];

    // 3. Render Boxes
    state.packedBoxes.forEach(box => {
      // If the API returns isPacked=false, we MUST NOT attempt to render them
      // at (0,0,0) as that will make them overlap with the front of the truck
      if (!box.isPacked) return;

      const color = box.isFragile ? 0xff4444 : 0x4444ff;
      const geometry = new THREE.BoxGeometry(box.width, box.height, box.length);
      const material = new THREE.MeshLambertMaterial({ color: color, transparent: true, opacity: 0.85 });
      const mesh = new THREE.Mesh(geometry, material);

      // Map algorithm coordinates to Three.js coordinates
      // X = Width, Y = Height (Z in algo), Z = Length (Y in algo)
      mesh.position.set(
        box.packedX + (box.width / 2),
        box.packedZ + (box.height / 2),
        box.packedY + (box.length / 2)
      );

      mesh.userData = { originalBox: box };
      mesh.add(new THREE.LineSegments(
        new THREE.EdgesGeometry(geometry),
        new THREE.LineBasicMaterial({ color: 0xffffff, transparent: true, opacity: 0.3 })
      ));

      state.cargoGroup.add(mesh);
      state.draggableMeshes.push(mesh);
    });

    // 4. Re-bind DragControls to the specific renderer and camera of this TruckViewport
    state.dragControls = new DragControls(state.draggableMeshes, state.camera, state.renderer.domElement);
    
    state.dragControls.addEventListener('dragstart', (event) => {
      state.orbitControls.enabled = false;
      (event.object as THREE.Mesh).material = new THREE.MeshLambertMaterial({ color: 0xffff00, opacity: 0.5, transparent: true });
    });

    state.dragControls.addEventListener('dragend', (event) => {
      state.orbitControls.enabled = true;
      const mesh = event.object as THREE.Mesh;
      const boxData = mesh.userData['originalBox'] as CargoBox;

      // Map back to algorithm space
      boxData.packedX = Math.round(mesh.position.x - (boxData.width / 2));
      boxData.packedZ = Math.round(mesh.position.y - (boxData.height / 2));
      boxData.packedY = Math.round(mesh.position.z - (boxData.length / 2));

      this.logisticsService.updateBoxCoordinates(boxData).subscribe();
    });
  }

  private drawTruckWireframe(group: THREE.Group) {
    const geometry = new THREE.BoxGeometry(this.TRUCK_W, this.TRUCK_H, this.TRUCK_L);
    geometry.translate(this.TRUCK_W / 2, this.TRUCK_H / 2, this.TRUCK_L / 2);

    const edges = new THREE.EdgesGeometry(geometry);
    const material = new THREE.LineBasicMaterial({ color: 0x00ff00, linewidth: 2, opacity: 0.3, transparent: true });
    const truckWireframe = new THREE.LineSegments(edges, material);

    group.add(truckWireframe);

    const floorGeometry = new THREE.PlaneGeometry(this.TRUCK_W, this.TRUCK_L);
    floorGeometry.rotateX(-Math.PI / 2);
    floorGeometry.translate(this.TRUCK_W / 2, 0, this.TRUCK_L / 2);
    const floorMaterial = new THREE.MeshBasicMaterial({ color: 0x333333, side: THREE.DoubleSide, transparent: true, opacity: 0.5 });
    const floorMesh = new THREE.Mesh(floorGeometry, floorMaterial);

    group.add(floorMesh);

    const doorGeometry = new THREE.PlaneGeometry(this.TRUCK_W, this.TRUCK_H);
    doorGeometry.translate(this.TRUCK_W / 2, this.TRUCK_H / 2, this.TRUCK_L);

    const doorMaterial = new THREE.MeshBasicMaterial({
      color: 0xff8800,
      transparent: true,
      opacity: 0.15,
      side: THREE.DoubleSide
    });
    const doorMesh = new THREE.Mesh(doorGeometry, doorMaterial);

    const doorEdges = new THREE.EdgesGeometry(doorGeometry);
    const doorEdgeMaterial = new THREE.LineBasicMaterial({ color: 0xffaa00, linewidth: 3 });
    const doorWireframe = new THREE.LineSegments(doorEdges, doorEdgeMaterial);

    group.add(doorMesh);
    group.add(doorWireframe);
  }

  // Add this method to handle the "Load Current Plan" button
  public fetchCurrentPlan() {
    this.logisticsService.loadExistingPlan(1).subscribe({
      next: (data) => {
        const mappedBoxes = data.map(row => ({
          boxId: row.boxId || row.BoxId,
          stopSequence: row.sequenceNumber || row.SequenceNumber,
          length: row.lengthMm || row.LengthMm,
          width: row.widthMm || row.WidthMm,
          height: row.heightMm || row.HeightMm,
          weight: row.weightKg || row.WeightKg,
          isFragile: row.isFragile || row.IsFragile,
          isPacked: row.isPacked || row.IsPacked,
          packedX: row.packedX || row.PackedX,
          packedY: row.packedY || row.PackedY,
          packedZ: row.packedZ || row.PackedZ
        }));

        // For the demo, we'll arbitrarily split the existing DB data into the two trucks
        // based on sequence to visualize the restore feature.
        this.truckA.packedBoxes = mappedBoxes.filter(b => b.stopSequence <= 2);
        this.truckB.packedBoxes = mappedBoxes.filter(b => b.stopSequence > 2);

        this.renderBoxes(this.truckA);
        this.renderBoxes(this.truckB);
      },
      error: (err: any) => console.error('Failed to fetch existing plan', err)
    });
  }
}